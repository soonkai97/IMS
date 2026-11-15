using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Turns a module on or off for the installation. The flip is a settings row (<c>Module.&lt;code&gt;</c>), written under the <c>modules</c> lock so two flips never interleave. The decision is
/// <see cref="ModuleSwitchPolicy"/> (DDD-14): this handler takes the lock, reads the row, the modules that are on and the checks, in that order and nothing once a refusal is known, asks the policy at each step and maps each
/// refusal to the sentence and the status it always had. The row raises <see cref="ModuleSwitched"/>; its subscribers write the audit event (the settings table is not audited row by row: who, why, the state before and
/// after, and, when a module is turned on, how many roles get the module's permissions back, <see cref="ModuleSwitchAuditSubscriber"/>, in this transaction) and drop the cached switches once the flip has committed
/// (<see cref="ModuleSwitchedCacheInvalidator"/>). A module that is already in the requested state is a 200 that changes nothing and writes nothing. A switch only hides or
/// shows the surface; it never touches stock, so no posting reads it. A module may not be turned on while a module it requires is off, nor a module off while one that requires it is on (409 naming the other, <see cref="ModuleDependencies"/>). A module that holds data (open orders) may refuse to be turned off through its <see cref="IModuleDisableCheck"/>: a 409 with the count, nothing written.
/// </summary>
public sealed class SwitchModuleHandler(IRepository<SystemSetting> settings, IReadRepository<Role> roles, IModuleSwitches switches, IEnumerable<IModuleDisableCheck> disableChecks, IEnumerable<IModuleEnableCheck> enableChecks,
    ITransactionLocker locker, IUnitOfWork uow)
    : ICommandHandler<SwitchModuleCommand, ModuleSwitchResult>
{
    public Task<ModuleSwitchResult> HandleAsync(SwitchModuleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var module = ModuleCatalog.Find(cmd.Code) ?? throw new NotFoundException(Messages.ModuleNotFound);
        var wanted = cmd.Data.Enabled!.Value; // the validator refused a body without it

        await locker.LockModulesAsync(ct);
        var row = await settings.FirstOrDefaultAsync(new ModuleSettingSpec(module.Code), ct);
        var outcome = ModuleSwitchPolicy.Outcome(row is not null && SettingValues.IsTrue(row.Value), wanted);
        if (outcome == ModuleSwitchOutcome.Unchanged)
        {
            switches.Invalidate(); // nothing was switched, so no event and no after-commit handler: the drop stays here, as it always was
            return ModuleSwitchResult.From(ModuleDto.From(module, wanted), 0);
        }

        EnsureNotRefused(ModuleSwitchPolicy.ReasonRefusal(cmd.Data.Reason), module);
        var why = Reason.Of(cmd.Data.Reason, Messages.ModuleReasonRequired); // cannot refuse now: the policy has just said the reason is given
        var on = await EnabledModulesAsync(ct);
        EnsureNotRefused(ModuleSwitchPolicy.DependencyRefusal(module, outcome, on), module);
        var regaining = 0;
        if (outcome == ModuleSwitchOutcome.TurnOn)
        {
            await EnsureReadyAsync(module, ct);
            regaining = await RolesRegainingGrantsAsync(module.Code, ct);
        }
        else await EnsureNothingWouldBeOrphanedAsync(module, ct);

        if (row is null) settings.Add(SystemSetting.CreateModuleSwitch(module.Code, wanted, why, regaining));
        else row.SwitchModule(wanted, why, regaining);
        await uow.SaveChangesAsync(ct); // the audit subscriber runs inside this save; the cache is dropped after the transaction commits
        return ModuleSwitchResult.From(ModuleDto.From(module, wanted), regaining);
    }, ct);

    /// <summary>The modules that are on, read from the stored rows now (under the modules lock), not from the cached switches, which can be stale after another flip in the same instant.</summary>
    private async Task<IReadOnlySet<string>> EnabledModulesAsync(CancellationToken ct) =>
        (await settings.ListAsync(new ModuleSettingsSpec(), ct)).Where(s => SettingValues.IsTrue(s.Value)).Select(s => s.Key[SettingKeys.ModulePrefix.Length..]).ToHashSet(StringComparer.Ordinal);

    /// <summary>The sentence and the status of each refusal the policy can make from the request alone and the modules that are on: a missing or short reason is a 400, a module in the way a 409 naming it (nothing is written).</summary>
    private static void EnsureNotRefused(ModuleSwitchRefusal refusal, ModuleDefinition module)
    {
        switch (refusal.Reason)
        {
            case ModuleSwitchRefusalReason.None: return;
            case ModuleSwitchRefusalReason.ReasonRequired: throw new DomainException(Messages.ModuleReasonRequired);
            case ModuleSwitchRefusalReason.NeedsOtherModules: throw new ConflictException(Messages.ModuleNeedsOthers(module.Name, refusal.Modules.Select(m => m.Name)));
            case ModuleSwitchRefusalReason.NeededByOtherModules: throw new ConflictException(Messages.ModuleIsNeededBy(module.Name, refusal.Modules.Select(m => m.Name)));
            default: throw new ArgumentOutOfRangeException(nameof(refusal), refusal.Reason, "Not a refusal this step can make.");
        }
    }

    /// <summary>The live roles that hold a permission of the module: their grants, kept while it was off, are in force again from the next request.</summary>
    private async Task<int> RolesRegainingGrantsAsync(string moduleCode, CancellationToken ct)
    {
        var codes = PermissionCatalog.All.Where(p => p.Module == moduleCode).Select(p => p.Code).ToList();
        return codes.Count == 0 ? 0 : await roles.CountAsync(new RolesHoldingPermissionsSpec(codes), ct);
    }

    /// <summary>Asks the checks that say a module may not be turned on yet (409 with the sentence that names the fix); a module without one is never refused. The first check that refuses ends the asking.</summary>
    private async Task EnsureReadyAsync(ModuleDefinition module, CancellationToken ct)
    {
        foreach (var check in enableChecks.Where(c => c.ModuleCode == module.Code))
        {
            var sentence = await check.RefusalAsync(ct);
            if (ModuleSwitchPolicy.ReadinessRefusal(sentence).Reason == ModuleSwitchRefusalReason.NotReady) throw new ConflictException(sentence!); // a refusal is only ever made for a sentence
        }
    }

    /// <summary>Asks the module's checks, if it has any, before the switch is written; a module without one is never refused. The first check that counts a blocker ends the asking.</summary>
    private async Task EnsureNothingWouldBeOrphanedAsync(ModuleDefinition module, CancellationToken ct)
    {
        foreach (var check in disableChecks.Where(c => c.ModuleCode == module.Code))
        {
            var blockers = await check.CountBlockersAsync(ct);
            if (ModuleSwitchPolicy.OpenDataRefusal(blockers).Reason == ModuleSwitchRefusalReason.HasOpenData) throw new ConflictException(Messages.ModuleHasOpenData(module.Name, check.Describe(blockers)));
        }
    }
}
