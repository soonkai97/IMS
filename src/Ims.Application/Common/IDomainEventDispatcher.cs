namespace Ims.Application;

/// <summary>
/// Takes the domain events that the tracked aggregates have raised and hands them to their in-transaction handlers. The unit of work calls it at the top of every save, so a use case rarely
/// calls it; a use case calls it only to make the handlers run before a later step of its own (a price-change audit that must follow the order's own audit row).
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Collects the events of every tracked aggregate, clears them from the aggregates, and runs the in-transaction handlers in rounds: events are ordered by the sequence the domain gave them,
    /// events raised by handlers go in the next round, and a round that still finds events after 100 rounds throws. Calling it again finds nothing new. It stages changes but never saves.
    /// </summary>
    Task DispatchAsync(CancellationToken ct = default);
}
