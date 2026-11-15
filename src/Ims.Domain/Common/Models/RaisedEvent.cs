namespace Ims.Domain;

/// <summary>
/// A domain event together with the place it took in the order of everything the domain raised: <paramref name="Sequence"/> comes from one counter for the whole process, so events of two
/// aggregates can be put in the order the domain raised them (the change tracker gives no order). A struct on purpose: the persistence layer discovers a class held in a collection as an entity type, a struct it leaves alone, so no mapping is needed. Only the sequence is new; <see cref="IAggregateRoot.DomainEvents"/> still lists the bare events.
/// </summary>
public readonly record struct RaisedEvent(long Sequence, IDomainEvent Event)
{
    private static long _last;

    /// <summary>Numbers an event as it is raised. The one counter of the process: a root that raises events (derived from <see cref="AggregateRoot"/> or implementing <see cref="IAggregateRoot"/>) stamps its events here.</summary>
    internal static RaisedEvent Stamp(IDomainEvent domainEvent) => new(Interlocked.Increment(ref _last), domainEvent);
}
