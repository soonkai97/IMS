namespace Ims.Domain;

/// <summary>
/// An aggregate root: the one entity through which an aggregate changes, and the one that raises its domain events. It keeps the events it raised until the unit of work takes them and clears them.
/// Roots with a public id derive from <see cref="AggregateRoot"/>; a root without one (a stock balance, a document sequence) implements this interface directly.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>The events raised since the root was loaded or last cleared, oldest first. Read-only: only the root raises them.</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Forgets the events once they have been taken (by the unit of work after it handed them to their subscribers).</summary>
    void ClearDomainEvents();
}
