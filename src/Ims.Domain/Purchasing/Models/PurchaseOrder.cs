namespace Ims.Domain;

/// <summary>
/// A purchase order: what is asked of one supplier for one ship-to warehouse. It is its own aggregate and moves no stock (plan D2), so it never appears in the documents list, the
/// movements report or the accounting export. This slice (PL2.2) builds the Draft: create, replace header and lines, read. The columns of the approval workflow (submit, approve, close,
/// cancel) exist already so that the workflow slice needs no migration; no method writes them yet. Totals are computed from the lines, never stored.
/// </summary>
public class PurchaseOrder : PublicEntity
{
    private PurchaseOrder() { }

    public string No { get; private set; } = "";
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public int WarehouseId { get; private set; }
    public int SupplierId { get; private set; }
    public string? Remark { get; private set; }

    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? SubmittedBy { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public int? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public int? ClosedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>Set at submit time from the approval rules (workflow slice); false means a submitted order is released at once.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Set at submit time: only a role above Supervisor (the Owner) may approve it.</summary>
    public bool IsLarge { get; private set; }
    public string? RejectionReason { get; private set; }
    public int? RejectedBy { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? CancelReason { get; private set; }
    public PurchaseOrderCloseKind? CloseKind { get; private set; }
    public string? CloseReason { get; private set; }

    private readonly List<PurchaseOrderLine> _lines = [];

    /// <summary>Read-only view: lines change only through <see cref="ReplaceLines"/>, and only while the order is a Draft.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    // Navigation properties used by read-side projections.
    public Warehouse Warehouse { get; private set; } = null!;
    public Partner Supplier { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

    [VisibleWith(Permissions.CostView)]
    public decimal TotalAmount => Lines.Sum(l => l.Amount);

    /// <summary>Opens a new Draft. The number is assigned separately because allocation needs the database (BR-11).</summary>
    public static PurchaseOrder Create(Warehouse warehouse, Partner supplier, string? remark, int createdBy, DateTime now)
    {
        warehouse.EnsureUsable();
        supplier.EnsureUsableAsSupplier();

        return new PurchaseOrder
        {
            Warehouse = warehouse, WarehouseId = warehouse.Id, Supplier = supplier, SupplierId = supplier.Id,
            Remark = remark, CreatedBy = createdBy, CreatedAt = now
        };
    }

    /// <summary>The number is allocated once (BR-11) and never changes afterwards.</summary>
    public void AssignNumber(string no)
    {
        if (No.Length > 0) throw new DomainException(Messages.NumberAlreadyAssigned(No));
        No = no;
    }

    /// <summary>The supplier and the remark of a Draft. The warehouse never changes: it carries the number.</summary>
    public void UpdateHeader(Partner supplier, string? remark)
    {
        EnsureEditable();
        supplier.EnsureUsableAsSupplier();
        Supplier = supplier;
        SupplierId = supplier.Id;
        Remark = remark;
    }

    /// <summary>Replaces every line of a Draft, numbered 1 to n in the order given. Removed lines are deleted as orphans by the persistence layer.</summary>
    public void ReplaceLines(IEnumerable<PurchaseOrderLine> lines)
    {
        EnsureEditable();
        var replacement = lines.ToList();
        if (replacement.Count == 0) throw new DomainException(Messages.AddLine);
        if (replacement.Count > Limits.MaxDocumentLines) throw new DomainException(Messages.TooManyLines(Limits.MaxDocumentLines));
        for (var i = 0; i < replacement.Count; i++) replacement[i].Number(i + 1);
        _lines.Clear();
        _lines.AddRange(replacement);
    }

    public void EnsureEditable()
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException(Messages.PurchaseOrderNotEditable(No, Status));
    }
}
