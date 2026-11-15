namespace Ims.Domain;

/// <summary>
/// A purchase order: what is asked of one supplier for one ship-to warehouse. It is its own aggregate and moves no stock (plan D2), so it never appears in the documents list, the
/// movements report. PL2.2 built the Draft (create, replace header and lines, read); PL2.3 adds the workflow of the two approval tiers: Draft -> Submitted -> Approved
/// (or straight to Approved when no rule fires), Submitted -> Draft by a rejection or a recall, and Draft, Submitted or Approved -> Cancelled. Closing (automatic and short) comes with the
/// receipts (PL2.5). Totals are computed from the lines, never stored.
/// </summary>
/// <remarks>
/// An aggregate root (<see cref="AggregateRoot"/>, DDD-12): every change that the audit trail records raises one event once the change is made, carrying what the audit row prints (<c>Purchasing/Events</c>);
/// a refused change raises nothing. <c>PurchaseOrderAuditSubscriber</c> writes the rows. The edit of a Draft is two calls (<see cref="UpdateHeader"/>, then <see cref="ReplaceLines"/>) and one audit row, so it is
/// one event, raised by the second.
/// </remarks>
public sealed class PurchaseOrder : AggregateRoot
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

    /// <summary>The summary before <see cref="UpdateHeader"/> changed the header, kept until <see cref="ReplaceLines"/> completes the edit and raises <see cref="PurchaseOrderUpdated"/> with it. Not a column.</summary>
    private PurchaseOrderSummary? _summaryBeforeEdit;

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

    /// <summary>Whose it is to take back: the one rule of <see cref="DocumentOwnership"/>, given who last changed the row.</summary>
    public DocumentOwnership Ownership => new(CreatedBy, SubmittedBy, RecRevisor, Status == PurchaseOrderStatus.Draft);

    /// <summary>Only a Draft can be changed (header and lines). What a caller answers to a change of any other state is its own mapping: <see cref="EnsureEditable"/> says 400, an update handler says 409.</summary>
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    /// <summary>Opens a new Draft. The number is assigned separately because allocation needs the database (BR-11).</summary>
    public static PurchaseOrder Create(Warehouse warehouse, Partner supplier, string? remark, int createdBy, DateTime now)
    {
        warehouse.EnsureUsable();
        supplier.EnsureUsableAsSupplier();

        return new PurchaseOrder
        {
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Supplier = supplier,
            SupplierId = supplier.Id,
            Remark = remark,
            CreatedBy = createdBy,
            CreatedAt = now
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
        _summaryBeforeEdit ??= Summary();
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
        var isNew = _lines.Count == 0; // a Draft always has lines once it is created, so no lines yet means the create is being completed
        var before = _summaryBeforeEdit ?? Summary();
        for (var i = 0; i < replacement.Count; i++) replacement[i].Number(i + 1);
        _lines.Clear();
        _lines.AddRange(replacement);
        _summaryBeforeEdit = null;
        Raise(isNew ? new PurchaseOrderCreated(No, Summary()) : new PurchaseOrderUpdated(No, before, Summary()));
    }

    /// <summary>What the audit row of a create or an update prints about the order.</summary>
    private PurchaseOrderSummary Summary() => new(Status, WarehouseId, SupplierId, _lines.Count, TotalAmount);

    public void EnsureEditable()
    {
        if (!IsEditable)
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

        var before = Status;
        SubmittedBy = userId;
        SubmittedAt = utcNow;
        RequiresApproval = requiresApproval;
        IsLarge = requiresApproval && isLarge;
        RejectionReason = null;
        RejectedBy = null;
        RejectedAt = null;
        Status = requiresApproval ? PurchaseOrderStatus.Submitted : PurchaseOrderStatus.Approved;
        if (!requiresApproval) ApprovedAt = utcNow;
        Raise(new PurchaseOrderSubmitted(No, before, Status, RequiresApproval, IsLarge, Money.Rounded(TotalAmount)));
    }

    /// <summary>Approves a Submitted order; not by the person who submitted it unless self-approval is allowed (BR-07). Who may is <see cref="PurchaseOrderActionPolicy"/>.</summary>
    public void Approve(int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanApprove(Status));
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.PurchaseOrderSelfApproval));

        var before = Status;
        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = userId;
        ApprovedAt = utcNow;
        Raise(new PurchaseOrderApproved(No, before, Status, IsLarge));
    }

    /// <summary>Returns a Submitted order to Draft with the (trimmed) reason and who returned it. A review like approving: not by the submitter unless self-approval is allowed.</summary>
    public void Reject(string reason, int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanReject(Status));
        var why = Reason.Required(reason, Messages.RejectReasonRequired);
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.PurchaseOrderSelfRejection));

        var before = Status;
        var trimmed = why.Text;
        Status = PurchaseOrderStatus.Draft;
        RejectionReason = trimmed;
        RejectedBy = userId;
        RejectedAt = utcNow;
        ClearSubmission();
        Raise(new PurchaseOrderRejected(No, before, Status, trimmed));
    }

    /// <summary>Takes a Submitted order back to Draft so its submitter can fix it. Nothing was approved, so there is nothing to void; it is not a rejection (no reason is kept).</summary>
    public void Recall()
    {
        if (Status != PurchaseOrderStatus.Submitted)
            throw new DomainException(Messages.PurchaseOrderOnlySubmittedCanRecall(No, Status));

        var before = Status;
        var requiredApproval = RequiresApproval;
        Status = PurchaseOrderStatus.Draft;
        ClearSubmission();
        Raise(new PurchaseOrderRecalled(No, before, requiredApproval, Status));
    }

    /// <summary>
    /// Cancels a Draft, a Submitted or an Approved order. An Approved order was released to the supplier's side of the business, so cancelling it must say why (at least
    /// <see cref="Limits.ReasonMinLength"/> characters once trimmed); who approved and when stays as history. An order with a receipt open or posted against it is not cancelled (PL2.4); the handler refuses.
    /// </summary>
    public void Cancel(string? reason, bool byAnotherUser = false)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Approved))
            throw new DomainException(Messages.PurchaseOrderCannotCancel(Status));
        if (Status == PurchaseOrderStatus.Approved)
        {
            reason = Reason.Required(reason, Messages.PurchaseOrderCancelReasonRequired).Text;
        }
        if (Status == PurchaseOrderStatus.Draft && byAnotherUser) reason = Reason.Of(reason, Messages.CancelOthersDraftReasonRequired).Text; // a holder of modules.manage clearing a departed creator draft says why
        var before = Status;
        Status = PurchaseOrderStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Raise(new PurchaseOrderCancelled(No, before, Status, CancelReason));
    }

    /// <summary>
    /// Closes an Approved order because every line is fully received (plan D5): called by posting, in the same transaction as the receipt that brought in the last quantity. The poster is recorded;
    /// there is no reason to give. A reversal of a receipt later never reopens it. The receipt is named because the audit row of this close prints it.
    /// </summary>
    public void CloseReceived(int userId, string receiptNo, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Approved)
            throw new DomainException(Messages.PurchaseOrderCannotClose(Status));
        MarkClosed(PurchaseOrderCloseKind.Received, null, receiptNo, userId, utcNow);
    }

    /// <summary>Closes an Approved order short: what is still to come will not come. It needs a reason (at least <see cref="Limits.ReasonMinLength"/> characters once trimmed). Receipts already posted stay; the handler refuses while one is still open.</summary>
    public void CloseShort(string? reason, int userId, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Approved)
            throw new DomainException(Messages.PurchaseOrderCannotClose(Status));
        var why = Reason.Required(reason, Messages.PurchaseOrderCloseReasonRequired);
        MarkClosed(PurchaseOrderCloseKind.Short, why.Text, null, userId, utcNow);
    }

    private void MarkClosed(PurchaseOrderCloseKind kind, string? reason, string? receiptNo, int userId, DateTime utcNow)
    {
        var before = Status;
        Status = PurchaseOrderStatus.Closed;
        CloseKind = kind;
        CloseReason = reason;
        ClosedBy = userId;
        ClosedAt = utcNow;
        Raise(new PurchaseOrderClosed(No, before, Status, kind, reason, receiptNo));
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
