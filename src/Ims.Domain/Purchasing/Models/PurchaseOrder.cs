namespace Ims.Domain;

/// <summary>
/// A purchase order: what is asked of one supplier for one ship-to warehouse. It is its own aggregate and moves no stock (plan D2), so it never appears in the documents list, the
/// movements report or the accounting export. PL2.2 built the Draft (create, replace header and lines, read); PL2.3 adds the workflow of the two approval tiers: Draft -> Submitted -> Approved
/// (or straight to Approved when no rule fires), Submitted -> Draft by a rejection or a recall, and Draft, Submitted or Approved -> Cancelled. Closing (automatic and short) comes with the
/// receipts (PL2.5). Totals are computed from the lines, never stored.
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

    /// <summary>Set at submit time from the approval rules; false means a submitted order was released at once (Approved with no approver).</summary>
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

    /// <summary>The total base quantity: what a quantity rule of the approval rules is tested against.</summary>
    public decimal TotalQty => Lines.Sum(l => l.BaseQty);

    /// <summary>A Draft that a reviewer sent back (the reason is kept until the next submit).</summary>
    public bool IsReturned => Status == PurchaseOrderStatus.Draft && RejectionReason is not null;

    /// <summary>A Draft that no reviewer sent back: what a recall leaves behind, so a repeated recall (a double tap) finds it and changes nothing.</summary>
    public bool IsPlainDraft => Status == PurchaseOrderStatus.Draft && RejectionReason is null;

    /// <summary>Only an Approved order can be received against (a receipt can be created from it and saved against it); the other states cannot, whatever has been received.</summary>
    public bool CanBeReceived => Status == PurchaseOrderStatus.Approved;

    public PurchaseOrderActionContext ActionContext => new(Status, RequiresApproval, IsLarge, CreatedBy, SubmittedBy);

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

    /// <summary>
    /// Sends a Draft on. Every line needs a price above 0 (a draft may hold 0.00, a 0.00 order would pass every amount threshold unseen, plan D11). When an approval rule fired
    /// (<paramref name="requiresApproval"/>) the order waits as Submitted for the reviewer of its tier; otherwise it is released at once: Approved, with the submitter recorded and no approver.
    /// </summary>
    public void Submit(int userId, bool requiresApproval, bool isLarge, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException(Messages.PurchaseOrderOnlyDraftCanSubmit(Status));
        if (Lines.Where(l => l.UnitPrice <= 0).Select(l => l.LineNo).ToList() is { Count: > 0 } unpriced)
            throw new DomainException(Messages.PurchaseOrderLinesNeedPrice(unpriced));

        SubmittedBy = userId;
        SubmittedAt = utcNow;
        RequiresApproval = requiresApproval;
        IsLarge = requiresApproval && isLarge;
        RejectionReason = null;
        RejectedBy = null;
        RejectedAt = null;
        Status = requiresApproval ? PurchaseOrderStatus.Submitted : PurchaseOrderStatus.Approved;
        if (!requiresApproval) ApprovedAt = utcNow;
    }

    /// <summary>Approves a Submitted order; not by the person who submitted it unless self-approval is allowed (BR-07). Who may is <see cref="PurchaseOrderActionPolicy"/>.</summary>
    public void Approve(int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanApprove(Status));
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.PurchaseOrderSelfApproval));

        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = userId;
        ApprovedAt = utcNow;
    }

    /// <summary>Returns a Submitted order to Draft with the (trimmed) reason and who returned it. A review like approving: not by the submitter unless self-approval is allowed.</summary>
    public void Reject(string reason, int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanReject(Status));
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.RejectReasonRequired));
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.ReasonTooShort, Limits.ReasonMinLength));
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.PurchaseOrderSelfRejection));

        Status = PurchaseOrderStatus.Draft;
        RejectionReason = reason.Trim();
        RejectedBy = userId;
        RejectedAt = utcNow;
        ClearSubmission();
    }

    /// <summary>Takes a Submitted order back to Draft so its submitter can fix it. Nothing was approved, so there is nothing to void; it is not a rejection (no reason is kept).</summary>
    public void Recall()
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanRecall(No, Status));

        Status = PurchaseOrderStatus.Draft;
        ClearSubmission();
    }

    /// <summary>
    /// Cancels a Draft, a Submitted or an Approved order. An Approved order was released to the supplier's side of the business, so cancelling it must say why (at least
    /// <see cref="Limits.ReasonMinLength"/> characters once trimmed); who approved and when stays as history. An order with a receipt open or posted against it is not cancelled (PL2.4); the handler refuses.
    /// </summary>
    public void Cancel(string? reason)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Approved))
            throw new DomainException(Messages.PurchaseOrderCannotCancel(Status));
        if (Status == PurchaseOrderStatus.Approved)
        {
            BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.PurchaseOrderCancelReasonRequired));
            BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.ReasonTooShort, Limits.ReasonMinLength));
        }
        Status = PurchaseOrderStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>The submit-time facts no longer describe a Draft; submitting again assesses the rules from scratch.</summary>
    private void ClearSubmission()
    {
        SubmittedBy = null;
        SubmittedAt = null;
        RequiresApproval = false;
        IsLarge = false;
    }
}
