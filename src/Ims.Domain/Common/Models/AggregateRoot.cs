namespace Ims.Domain;

/// <summary>
/// A <see cref="PublicEntity"/> that is the root of an aggregate and raises domain events. A method of the root calls <see cref="Raise"/> once the change is valid and made, so a refused change raises nothing.
/// The list is get-only and has no property-shaped backing field, so the persistence layer does not map it (the same pattern as <c>Lines =&gt; _lines</c>, without a navigation): a row never stores an event.
/// </summary>
public abstract class AggregateRoot : PublicEntity, IAggregateRoot
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Records a fact about a change that has just been made. Only the root itself can call it.</summary>
    protected void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);
}
