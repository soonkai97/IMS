using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Turns a module on or off for the installation. The flip is a settings row (<c>Module.&lt;code&gt;</c>), written under the <c>modules</c> lock so two flips never interleave, and it
/// writes its own audit event (the settings table is not audited row by row): who, why, and the state before and after. A module that is already in the requested state is a 200
/// that changes nothing and writes nothing. A switch only hides or shows the surface; it never touches stock, so no posting reads it.
/// </summary>
public class SwitchModuleHandler(IRepository<SystemSetting> settings, IModuleSwitches switches, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<SwitchModuleCommand, ModuleDto>
{
    public Task<ModuleDto> HandleAsync(SwitchModuleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var module = ModuleCatalog.Find(cmd.Code) ?? throw new NotFoundException(Messages.ModuleNotFound);
        var wanted = cmd.Data.Enabled!.Value; // the validator refused a body without it

        await locker.LockModulesAsync(ct);
        var key = SettingKeys.ForModule(module.Code);
        var row = await settings.FirstOrDefaultAsync(new ModuleSettingSpec(module.Code), ct);
        var before = row is not null && SettingValues.IsTrue(row.Value);
        if (before != wanted)
        {
            BusinessRules.Ensure(new ReasonRequiredRule(cmd.Data.Reason, Messages.ModuleReasonRequired, Limits.ReasonMinLength));
            if (row is null) settings.Add(SystemSetting.Create(key, SettingValues.From(wanted)));
            else row.ChangeValue(SettingValues.From(wanted));
            audit.Record(wanted ? AuditActions.ModuleEnabled : AuditActions.ModuleDisabled, AuditEntities.SystemSetting, key,
                new { Module = module.Code, Enabled = before }, new { Module = module.Code, Enabled = wanted, Reason = cmd.Data.Reason.Trim() });
            await uow.SaveChangesAsync(ct);
        }
        switches.Invalidate();
        return ModuleDto.From(module, wanted);
    }, ct);
}
