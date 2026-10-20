using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Turns a module on or off for the installation. The flip is a settings row (<c>Module.&lt;code&gt;</c>), written under the <c>modules</c> lock so two flips never interleave, and it
/// writes its own audit event (the settings table is not audited row by row): who, why, the state before and after, and, when a module is turned on, how many roles get the module's
/// permissions back (roles keep them, hidden, while it is off). A request to turn on a module of <see cref="NotYetSwitchableModules"/> is a 400 before anything is locked or written. A module that is already in the requested state is a 200 that changes nothing and writes nothing. A switch only hides or
/// shows the surface; it never touches stock, so no posting reads it.
/// </summary>
public class SwitchModuleHandler(IRepository<SystemSetting> settings, IReadRepository<Role> roles, IModuleSwitches switches, NotYetSwitchableModules notYet, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<SwitchModuleCommand, ModuleSwitchResult>
{
    public Task<ModuleSwitchResult> HandleAsync(SwitchModuleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var module = ModuleCatalog.Find(cmd.Code) ?? throw new NotFoundException(Messages.ModuleNotFound);
        var wanted = cmd.Data.Enabled!.Value; // the validator refused a body without it
        // Before the lock, the write and the audit event: a module that is not safe to use yet is refused, whatever state it is in now (PL1.8 deletes the list).
        if (wanted && notYet.Codes.Contains(module.Code)) throw new ValidationException(Messages.ModuleNotYetSwitchableOn);

        await locker.LockModulesAsync(ct);
        var key = SettingKeys.ForModule(module.Code);
        var row = await settings.FirstOrDefaultAsync(new ModuleSettingSpec(module.Code), ct);
        var before = row is not null && SettingValues.IsTrue(row.Value);
        var regaining = 0;
        if (before != wanted)
        {
            BusinessRules.Ensure(new ReasonRequiredRule(cmd.Data.Reason, Messages.ModuleReasonRequired, Limits.ReasonMinLength));
            if (wanted) regaining = await RolesRegainingGrantsAsync(module.Code, ct);
            if (row is null) settings.Add(SystemSetting.Create(key, SettingValues.From(wanted)));
            else row.ChangeValue(SettingValues.From(wanted));
            audit.Record(wanted ? AuditActions.ModuleEnabled : AuditActions.ModuleDisabled, AuditEntities.SystemSetting, key,
                new { Module = module.Code, Enabled = before }, new { Module = module.Code, Enabled = wanted, Reason = cmd.Data.Reason.Trim(), RolesRegainingGrants = regaining });
            await uow.SaveChangesAsync(ct);
        }
        switches.Invalidate();
        return ModuleSwitchResult.From(ModuleDto.From(module, wanted), regaining);
    }, ct);

    /// <summary>The live roles that hold a permission of the module: their grants, kept while it was off, are in force again from the next request.</summary>
    private async Task<int> RolesRegainingGrantsAsync(string moduleCode, CancellationToken ct)
    {
        var codes = PermissionCatalog.All.Where(p => p.Module == moduleCode).Select(p => p.Code).ToList();
        return codes.Count == 0 ? 0 : await roles.CountAsync(new RolesHoldingPermissionsSpec(codes), ct);
    }
}
