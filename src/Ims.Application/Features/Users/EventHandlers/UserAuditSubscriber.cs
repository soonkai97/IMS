using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// Writes the audit row of a user created or saved (DDD-15, docs/architecture/ddd-roadmap.md 2.4, 2.5). It runs INSIDE the transaction of the use case, before the unit of work saves, so the row is written by the same
/// save as the change and commits or rolls back with it (AUTH-03). It reads ONLY the facts of the event (never an entity, never a <c>REC_*</c> column, never the password or its hash: the event carries neither) and stages
/// the row through <see cref="IAuditTrail"/> as the handlers did before: the same action, entity type, entity id (the public id of the user), the same Before and After keys in the same order, and the actor and the time
/// are read from the current user and the clock at this moment. The password change of a signed-in person (<c>user.password-changed</c>) and the sign-in rows stay with the Auth handlers.
/// </summary>
public sealed class UserAuditSubscriber(IAuditTrail audit) : IAppService, IDomainEventHandler<UserCreated>, IDomainEventHandler<UserUpdated>
{
    public Task HandleAsync(UserCreated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.UserCreated, AuditEntities.User, domainEvent.UserId.ToString(), after: new { domainEvent.Username, Role = domainEvent.RoleCode, domainEvent.AllWarehouses });
        return Task.CompletedTask;
    }

    public Task HandleAsync(UserUpdated domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.UserUpdated, AuditEntities.User, domainEvent.UserId.ToString(), Summary(domainEvent.Before), Summary(domainEvent.After));
        return Task.CompletedTask;
    }

    /// <summary>Both sides of an update, keys in the order the handler always wrote them.</summary>
    private static object Summary(UserSummary summary) => new { summary.Username, Role = summary.RoleCode, summary.AllWarehouses, summary.IsActive, Warehouses = summary.WarehouseIds };
}
