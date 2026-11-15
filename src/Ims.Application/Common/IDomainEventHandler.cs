using Ims.Domain;

namespace Ims.Application;

/// <summary>
/// Reacts to a domain event INSIDE the transaction of the use case, before the unit of work saves (docs/architecture/ddd-roadmap.md 2.5). What it stages (an audit row, a change to another
/// entity) is written by the same save and commits or rolls back with the business change; a handler that throws rolls the whole transaction back.
/// A handler may read and stage changes and may raise further events through an aggregate (they are delivered in the next round). It must never call <c>IUnitOfWork.SaveChangesAsync</c> (the
/// dispatcher is running inside that call, so it throws) and never take an <see cref="ITransactionLocker"/> lock: the use case already holds its locks in the global order.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}
