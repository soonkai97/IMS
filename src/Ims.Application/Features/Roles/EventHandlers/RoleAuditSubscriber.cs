using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Writes the audit row of every change to a role (DDD-15, docs/architecture/ddd-roadmap.md 2.4, 2.5). It runs INSIDE the transaction of the use case, before the unit of work saves, so the row is written by the same
/// save as the change and commits or rolls back with it (AUTH-03). It reads ONLY the facts of the event (never an entity, never a <c>REC_*</c> column) and stages the row through <see cref="IAuditTrail"/> as the handlers
/// did before: the same action, entity type, entity id (the public id of the role), the same Before and After keys in the same order, and the actor and the time are read from the current user and the clock at this moment,
/// as <c>AuditTrail.Record</c> does. The row-level rows of the persistence layer (Role, RolePermission) are not these rows and are written as before.
/// </summary>
public sealed class RoleAuditSubscriber(IAuditTrail audit) : IAppService,
    IDomainEventHandler<RoleCreated>, IDomainEventHandler<RoleUpdated>, IDomainEventHandler<RolePermissionsChanged>, IDomainEventHandler<RoleDeleted>
{
    public Task HandleAsync(RoleCreated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.RoleCreated, AuditEntities.Role, domainEvent.RoleId.ToString(), after: new { domainEvent.Code, domainEvent.Name, domainEvent.Permissions, domainEvent.CopiedFrom });
        return Task.CompletedTask;
    }

    public Task HandleAsync(RoleUpdated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.RoleUpdated, AuditEntities.Role, domainEvent.RoleId.ToString(),
            new { domainEvent.Before.Name, domainEvent.Before.Description, domainEvent.Before.IsActive },
            new { domainEvent.Code, domainEvent.After.Name, domainEvent.After.Description, domainEvent.After.IsActive });
        return Task.CompletedTask;
    }

    public Task HandleAsync(RolePermissionsChanged domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.RolePermissionsChanged, AuditEntities.Role, domainEvent.RoleId.ToString(), new { domainEvent.Code, domainEvent.Removed }, new { domainEvent.Code, domainEvent.Added });
        return Task.CompletedTask;
    }

    public Task HandleAsync(RoleDeleted domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.RoleDeleted, AuditEntities.Role, domainEvent.RoleId.ToString(), new { domainEvent.Code, domainEvent.Name, domainEvent.Permissions });
        return Task.CompletedTask;
    }
}
