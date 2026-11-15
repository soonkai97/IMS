using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Writes the audit row of a module switch (DDD-14, docs/architecture/ddd-roadmap.md 2.4, 2.5): who, why, the state before and after and, when a module is turned on, how many roles get its permissions back. It runs INSIDE the
/// transaction of the switch, before the unit of work saves, so the row is written by the same save as the flip and commits or rolls back with it (AUTH-03). It reads only the facts of the event and stages the row through
/// <see cref="IAuditTrail"/> as <c>SwitchModuleHandler</c> did: the same action, entity type, entity id (the settings key), the same Before and After keys in the same order; the actor and the time are read from the current
/// user and the clock at this moment, as <c>AuditTrail.Record</c> does.
/// </summary>
public sealed class ModuleSwitchAuditSubscriber(IAuditTrail audit) : IAppService, IDomainEventHandler<ModuleSwitched>
{
    public Task HandleAsync(ModuleSwitched domainEvent, CancellationToken ct)
    {
        audit.Record(domainEvent.EnabledAfter ? AuditActions.ModuleEnabled : AuditActions.ModuleDisabled, AuditEntities.SystemSetting, SettingKeys.ForModule(domainEvent.ModuleCode),
            new { Module = domainEvent.ModuleCode, Enabled = domainEvent.EnabledBefore },
            new { Module = domainEvent.ModuleCode, Enabled = domainEvent.EnabledAfter, domainEvent.Reason, domainEvent.RolesRegainingGrants });
        return Task.CompletedTask;
    }
}
