namespace Ims.Domain;

/// <summary>An audited row with an integer key assigned by the database.</summary>
public abstract class Entity : AuditableEntity
{
    /// <summary>Assigned by the database. Never set by application code.</summary>
    public int Id { get; protected set; }
}
