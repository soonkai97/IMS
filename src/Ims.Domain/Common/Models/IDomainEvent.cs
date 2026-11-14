namespace Ims.Domain;

/// <summary>
/// Marks a domain event: a fact that an aggregate root raised about a change that has happened (an order was submitted, a document was posted). It is a sealed record named in the past tense that carries
/// facts (ids, the status before and after, a reason, value objects) and never an entity, because a subscriber reads it after the unit of work has moved on. The interface has no members on purpose; it exists so that
/// <c>EventDesignTests</c> can find every event and hold it to one design. Nothing subscribes yet: the dispatcher and the first subscriber (the audit trail) come with the next slice of docs/architecture/ddd-roadmap.md.
/// </summary>
public interface IDomainEvent;
