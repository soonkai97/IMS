namespace Ims.Domain;

/// <summary>
/// A sales order: what a customer is to be delivered from one ship-from warehouse. It is its own aggregate and moves no stock (plan D1), so it never appears in the documents list or the
/// movements report. PL3.2 builds the Draft (create, replace header and lines, read); the workflow of the two approval tiers (submit, approve, reject, recall, cancel) comes with PL3.3 and
/// the deliveries with PL3.5 to PL3.7. The columns of that workflow exist already so those slices need no migration. Totals are computed from the lines, never stored.
/// </summary>
public class SalesOrder : PublicEntity
{
    private SalesOrder() { }

    public string No { get; private set; } = "";
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public int WarehouseId { get; private set; }
    public int CustomerId { get; private set; }
    public string? Remark { get; private set; }

    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? SubmittedBy { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public int? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public int? ClosedBy { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    /// <summary>Set at submit time from the approval rules; false means a submitted order was released at once.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Set at submit time: only a role above Supervisor (the Owner) may approve it.</summary>
    public bool IsLarge { get; private set; }
    public string? RejectionReason { get; private set; }
    public int? RejectedBy { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? CancelReason { get; private set; }
    public SalesOrderCloseKind? CloseKind { get; private set; }
    public string? CloseReason { get; private set; }

    private readonly List<SalesOrderLine> _lines = [];

    /// <summary>Read-only view: lines change only through <see cref="ReplaceLines"/>, and only while the order is a Draft.</summary>
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    // Navigation properties used by read-side projections.
    public Warehouse Warehouse { get; private set; } = null!;
    public Partner Customer { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

    [VisibleWith(Permissions.SoManage)]
    public decimal TotalSellingAmount => Lines.Sum(l => l.SellingAmount);

    /// <summary>The total base quantity: what a quantity rule of the approval rules is tested against.</summary>
    public decimal TotalQty => Lines.Sum(l => l.BaseQty);

    /// <summary>Opens a new Draft. The number is assigned separately because allocation needs the database (BR-11).</summary>
    public static SalesOrder Create(Warehouse warehouse, Partner customer, string? remark, int createdBy, DateTime now)
    {
        warehouse.EnsureUsable();
        customer.EnsureUsableFor(DocumentType.Issue); // an Issue goes to a Customer

        return new SalesOrder
        {
            Warehouse = warehouse, WarehouseId = warehouse.Id, Customer = customer, CustomerId = customer.Id,
            Remark = remark, CreatedBy = createdBy, CreatedAt = now
        };
    }

    /// <summary>The number is allocated once (BR-11) and never changes afterwards.</summary>
    public void AssignNumber(string no)
    {
        if (No.Length > 0) throw new DomainException(Messages.NumberAlreadyAssigned(No));
        No = no;
    }

    /// <summary>The customer and the remark of a Draft. The warehouse never changes: it carries the number.</summary>
    public void UpdateHeader(Partner customer, string? remark)
    {
        EnsureEditable();
        customer.EnsureUsableFor(DocumentType.Issue);
        Customer = customer;
        CustomerId = customer.Id;
        Remark = remark;
    }

    /// <summary>Replaces every line of a Draft, numbered 1 to n in the order given. Removed lines are deleted as orphans by the persistence layer.</summary>
    public void ReplaceLines(IEnumerable<SalesOrderLine> lines)
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
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException(Messages.SalesOrderNotEditable(No, Status));
    }
}
