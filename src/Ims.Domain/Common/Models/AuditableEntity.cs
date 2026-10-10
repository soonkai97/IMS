namespace Ims.Domain;

/// <summary>
/// The standard audit columns every table carries. They are stamped by the persistence layer on save (never set by use cases):
/// REC_CREATOR / REC_CREDATE when a row is inserted, REC_REVISOR / REC_REVDATE on every change, and REC_DELETED when a row is
/// deleted (rows are never physically removed, except by an archive job). ROW_VERSION is the optimistic-concurrency token.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>REC_DELETED: soft-delete flag. Deleted rows are hidden from every query by a global filter.</summary>
    public bool RecDeleted { get; private set; }

    /// <summary>REC_CREATOR: id of the user who created the row; null when the system did (seed, background work).</summary>
    public int? RecCreator { get; private set; }

    /// <summary>REC_CREDATE: when the row was created (UTC).</summary>
    public DateTime RecCreDate { get; private set; }

    /// <summary>REC_REVISOR: id of the user who last changed (or deleted) the row.</summary>
    public int? RecRevisor { get; private set; }

    /// <summary>REC_REVDATE: when the row was last changed or deleted (UTC).</summary>
    public DateTime? RecRevDate { get; private set; }

    /// <summary>ROW_VERSION: concurrency token maintained by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Marks the row deleted. The persistence layer also turns any physical delete of a tracked row into this.</summary>
    public void MarkDeleted() => RecDeleted = true;
}
