namespace Ims.Domain;

/// <summary>
/// An <see cref="Entity"/> that a client addresses (a user, a warehouse, a product, a document ...). Besides the integer key the database clusters on, it carries a
/// <see cref="PublicId"/> that is meant to leave the system: it is random, so two of them reveal nothing about how many rows exist between them (decision D2 in
/// <c>docs/architecture/adr-guid-keys.md</c>). The integer key stays internal.
/// </summary>
public abstract class PublicEntity : Entity
{
    /// <summary>
    /// A random (version 4) GUID, made when the object is created and never changed or set from outside; the persistence layer only reads it back.
    /// Unique among ALL rows, soft-deleted ones included, so an id is never handed out twice (<c>UX_&lt;Table&gt;_PublicId</c>, not filtered by REC_DELETED).
    /// </summary>
    public Guid PublicId { get; private set; } = Guid.NewGuid();
}
