namespace Ims.Domain;

/// <summary>
/// A sales order: what a customer is to be delivered from one ship-from warehouse. It is its own aggregate and moves no stock (plan D1), so it never appears in the documents list or the
/// movements report. PL3.2 built the Draft (create, replace header and lines, read); PL3.3 adds the workflow of the two approval tiers: Draft -> Submitted -> Approved (or straight to
/// Approved when no rule fires), Submitted -> Draft by a rejection or a recall, and Draft, Submitted or Approved -> Cancelled. Closing and the deliveries come with PL3.5 to PL3.7. Totals are computed from the lines, never stored.
/// </summary>
/// <remarks>
/// It is the first aggregate root (<see cref="AggregateRoot"/>): every change that the audit trail records raises one event once the change is made, carrying what the audit row prints (<c>Sales/Events</c>);
/// a refused change raises nothing. Nobody listens yet. The edit of a Draft is two calls (<see cref="UpdateHeader"/>, then <see cref="ReplaceLines"/>) and one audit row, so it is one event, raised by the second.
/// </remarks>
public sealed class SalesOrder : AggregateRoot
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

    /// <summary>The summary before <see cref="UpdateHeader"/> changed the header, kept until <see cref="ReplaceLines"/> completes the edit and raises <see cref="SalesOrderUpdated"/> with it. Not a column.</summary>
    private SalesOrderSummary? _summaryBeforeEdit;

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
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Customer = customer,
            CustomerId = customer.Id,
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

    /// <summary>The customer and the remark of a Draft. The warehouse never changes: it carries the number.</summary>
    public void UpdateHeader(Partner customer, string? remark)
    {
        EnsureEditable();
        customer.EnsureUsableFor(DocumentType.Issue);
        _summaryBeforeEdit ??= Summary();
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
        var isNew = _lines.Count == 0; // a Draft always has lines once it is created, so no lines yet means the create is being completed
        var before = _summaryBeforeEdit ?? Summary();
        for (var i = 0; i < replacement.Count; i++) replacement[i].Number(i + 1);
        _lines.Clear();
        _lines.AddRange(replacement);
        _summaryBeforeEdit = null;
        Raise(isNew ? new SalesOrderCreated(No, Summary()) : new SalesOrderUpdated(No, before, Summary()));
    }

    /// <summary>What the audit row of a create or an update prints about the order.</summary>
    private SalesOrderSummary Summary() => new(Status, WarehouseId, CustomerId, _lines.Count, Money.Rounded(TotalSellingAmount));

    public void EnsureEditable()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException(Messages.SalesOrderNotEditable(No, Status));
    }

    /// <summary>A Draft that a reviewer sent back (the reason is kept until the next submit).</summary>
    public bool IsReturned => Status == SalesOrderStatus.Draft && RejectionReason is not null;

    /// <summary>A Draft that no reviewer sent back: what a recall leaves behind, so a repeated recall (a double tap) finds it and changes nothing.</summary>
    public bool IsPlainDraft => Status == SalesOrderStatus.Draft && RejectionReason is null;

    /// <summary>Only an Approved order can be delivered against (a delivery can be created from it and saved against it); the other states cannot, whatever has been delivered.</summary>
    public bool CanBeDelivered => Status == SalesOrderStatus.Approved;

    public SalesOrderActionContext ActionContext => new(Status, RequiresApproval, IsLarge, CreatedBy, SubmittedBy);

    /// <summary>
    /// Sends a Draft on. Every line needs a price above 0 (a draft may hold 0.00, a 0.00 order would pass every amount threshold unseen, plan D15). When an approval rule fired
    /// (<paramref name="requiresApproval"/>) the order waits as Submitted for the reviewer of its tier; otherwise it is released at once: Approved, with the submitter recorded and no approver.
    /// </summary>
    public void Submit(int userId, bool requiresApproval, bool isLarge, DateTime utcNow)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException(Messages.SalesOrderOnlyDraftCanSubmit(Status));
        if (Lines.Count == 0)
            throw new DomainException(Messages.SalesOrderNeedsLines);
        if (Lines.Where(l => l.SellingPrice <= 0).Select(l => l.LineNo).ToList() is { Count: > 0 } unpriced)
            throw new DomainException(Messages.SalesOrderLinesNeedPrice(unpriced));

        var before = Status;
        SubmittedBy = userId;
        SubmittedAt = utcNow;
        RequiresApproval = requiresApproval;
        IsLarge = requiresApproval && isLarge;
        RejectionReason = null;
        RejectedBy = null;
        RejectedAt = null;
        Status = requiresApproval ? SalesOrderStatus.Submitted : SalesOrderStatus.Approved;
        if (!requiresApproval) ApprovedAt = utcNow;
        Raise(new SalesOrderSubmitted(No, before, Status, RequiresApproval, IsLarge, Money.Rounded(TotalSellingAmount)));
    }

    /// <summary>Approves a Submitted order; not by the person who submitted it unless self-approval is allowed (BR-07). Who may is <see cref="SalesOrderActionPolicy"/>.</summary>
    public void Approve(int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != SalesOrderStatus.Submitted)
            throw new DomainException(Messages.SalesOrderOnlySubmittedCanApprove(Status));
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.SalesOrderSelfApproval));

        var before = Status;
        Status = SalesOrderStatus.Approved;
        ApprovedBy = userId;
        ApprovedAt = utcNow;
        Raise(new SalesOrderApproved(No, before, Status, IsLarge));
    }

    /// <summary>Returns a Submitted order to Draft with the (trimmed) reason and who returned it. A review like approving: not by the submitter unless self-approval is allowed.</summary>
    public void Reject(string reason, int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != SalesOrderStatus.Submitted)
            throw new DomainException(Messages.SalesOrderOnlySubmittedCanReject(Status));
        var why = Reason.Required(reason, Messages.RejectReasonRequired);
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.SalesOrderSelfRejection));

        var before = Status;
        var trimmed = why.Text;
        Status = SalesOrderStatus.Draft;
        RejectionReason = trimmed;
        RejectedBy = userId;
        RejectedAt = utcNow;
        ClearSubmission();
        Raise(new SalesOrderRejected(No, before, Status, trimmed));
    }

    /// <summary>Takes a Submitted order back to Draft so its submitter can fix it. Nothing was approved, so there is nothing to void; it is not a rejection (no reason is kept).</summary>
    public void Recall()
    {
        if (Status != SalesOrderStatus.Submitted)
            throw new DomainException(Messages.SalesOrderOnlySubmittedCanRecall(No, Status));

        var before = Status;
        var requiredApproval = RequiresApproval;
        Status = SalesOrderStatus.Draft;
        ClearSubmission();
        Raise(new SalesOrderRecalled(No, before, requiredApproval, Status));
    }

    /// <summary>
    /// Cancels a Draft, a Submitted or an Approved order. An Approved order was released to the customer's side of the business, so cancelling it must say why (at least
    /// <see cref="Limits.ReasonMinLength"/> characters once trimmed); who approved and when stays as history. An order with a delivery open or posted against it is not cancelled (PL3.6); the handler will refuse.
    /// </summary>
    public void Cancel(string? reason, bool byAnotherUser = false)
    {
        if (Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Submitted or SalesOrderStatus.Approved))
            throw new DomainException(Messages.SalesOrderCannotCancel(Status));
        if (Status == SalesOrderStatus.Approved)
        {
            reason = Reason.Required(reason, Messages.SalesOrderCancelReasonRequired).Text;
        }
        if (Status == SalesOrderStatus.Draft && byAnotherUser) reason = Reason.Of(reason, Messages.CancelOthersDraftReasonRequired).Text; // a holder of modules.manage clearing a departed creator draft says why
        var before = Status;
        Status = SalesOrderStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Raise(new SalesOrderCancelled(No, before, Status, CancelReason));
    }

    /// <summary>
    /// Closes an Approved order because every line is fully delivered (plan D7): called by posting, in the same transaction as the delivery that took out the last quantity. The poster is recorded;
    /// there is no reason to give. A reversal of a delivery later never reopens it. The delivery is named because the audit row of this close prints it.
    /// </summary>
    public void CloseDelivered(int userId, string deliveryNo, DateTime utcNow)
    {
        if (Status != SalesOrderStatus.Approved)
            throw new DomainException(Messages.SalesOrderCannotClose(Status));
        MarkClosed(SalesOrderCloseKind.Delivered, null, deliveryNo, userId, utcNow);
    }

    /// <summary>Closes an Approved order short: what is still to go out will not go. It needs a reason (at least <see cref="Limits.ReasonMinLength"/> characters once trimmed). Deliveries already posted stay; the handler refuses while one is still open.</summary>
    public void CloseShort(string? reason, int userId, DateTime utcNow)
    {
        if (Status != SalesOrderStatus.Approved)
            throw new DomainException(Messages.SalesOrderCannotClose(Status));
        var why = Reason.Required(reason, Messages.SalesOrderCloseReasonRequired);
        MarkClosed(SalesOrderCloseKind.Short, why.Text, null, userId, utcNow);
    }

    private void MarkClosed(SalesOrderCloseKind kind, string? reason, string? deliveryNo, int userId, DateTime utcNow)
    {
        var before = Status;
        Status = SalesOrderStatus.Closed;
        CloseKind = kind;
        CloseReason = reason;
        ClosedBy = userId;
        ClosedAt = utcNow;
        Raise(new SalesOrderClosed(No, before, Status, kind, reason, deliveryNo));
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
