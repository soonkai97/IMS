namespace Ims.Domain;

/// <summary>
/// A receipt or issue and its Figure 5 state machine: Draft -> Submitted -> (Approved) -> Posted -> Reversed, or Cancelled.
/// Posting is the irreversible step: a posted document is never edited, only reversed.
/// All state changes go through the methods below, so an illegal transition cannot happen.
/// </summary>
public class StockDocument : Entity
{
    private StockDocument() { }

    public DocumentType Type { get; private set; }
    public string No { get; private set; } = "";
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public int WarehouseId { get; private set; }
    public int? PartnerId { get; private set; }
    public IssuePurpose? Purpose { get; private set; }
    public string? Remark { get; private set; }

    public int CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int? SubmittedBy { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public int? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public int? PostedBy { get; private set; }
    public DateTime? PostedAt { get; private set; }

    /// <summary>Set at submit time from the approval rules; false means a submitted document may be posted directly.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Set at submit time: an approval rule that only a role above Supervisor (for example the Owner) may satisfy fired.</summary>
    public bool IsLarge { get; private set; }

    /// <summary>Set at submit time: an issue was submitted although stock is short (negative stock is allowed), so the approver must give an override reason when approving.</summary>
    public bool RequiresOverride { get; private set; }

    public string? RejectionReason { get; private set; }

    /// <summary>Who returned the document to Draft and when. Cleared when it is submitted again.</summary>
    public int? RejectedBy { get; private set; }
    public DateTime? RejectedAt { get; private set; }

    /// <summary>A Draft that a reviewer sent back: the creator sees it as "Returned" until it is submitted again.</summary>
    public bool IsReturned => Status == DocumentStatus.Draft && RejectionReason is not null;
    public string? CancelReason { get; private set; }
    public string? ReversalReason { get; private set; }

    /// <summary>When the document was reversed (UTC). The movements report dates the reversal row with it.</summary>
    public DateTime? ReversedAt { get; private set; }
    public int? ReversesDocumentId { get; private set; }

    /// <summary>Why the approver allowed stock to go below zero. Entered when approving (BR-03), required when <see cref="RequiresOverride"/> is set, and read by the posting run.</summary>
    public string? NegativeStockOverrideReason { get; private set; }

    private readonly List<StockDocumentLine> _lines = [];

    /// <summary>Read-only view: lines change only through <see cref="AddLines"/> and <see cref="ReplaceLines"/>, and only while the document is a Draft.</summary>
    public IReadOnlyList<StockDocumentLine> Lines => _lines;

    // Navigation properties used by read-side projections.
    public Warehouse Warehouse { get; private set; } = null!;
    public Partner? Partner { get; private set; }
    public User CreatedByUser { get; private set; } = null!;
    public User? SubmittedByUser { get; private set; }
    public User? ApprovedByUser { get; private set; }
    public User? PostedByUser { get; private set; }
    public User? RejectedByUser { get; private set; }

    public decimal TotalQty => Lines.Sum(l => l.BaseQty);
    [VisibleWith(Permissions.CostView)]
    public decimal TotalAmount => Lines.Sum(l => l.Amount);

    public bool CanPost => Status == DocumentStatus.Approved ||
                           (Status == DocumentStatus.Submitted && !RequiresApproval);

    /// <summary>The facts <see cref="DocumentActionPolicy"/> needs to say what a user may do with this document.</summary>
    public DocumentActionContext ActionContext => new(Status, RequiresApproval, IsLarge, CreatedBy, SubmittedBy);

    /// <summary>Opens a new Draft. The number is assigned separately because allocation needs the database (BR-11).</summary>
    public static StockDocument Create(DocumentType type, Warehouse warehouse, Partner? partner, IssuePurpose? purpose,
        string? remark, int createdBy, DateTime now)
    {
        if (type is not (DocumentType.Receipt or DocumentType.Issue)) throw new DomainException(Messages.OnlyReceiptAndIssue);
        warehouse.EnsureUsable();
        partner?.EnsureUsableFor(type);

        return new StockDocument
        {
            Type = type, Warehouse = warehouse, WarehouseId = warehouse.Id, Partner = partner, PartnerId = partner?.Id,
            Purpose = type == DocumentType.Issue ? purpose ?? throw new DomainException(Messages.PurposeRequired) : null,
            Remark = remark, CreatedBy = createdBy, CreatedAt = now
        };
    }

    /// <summary>The number is allocated once (BR-11) and never changes afterwards.</summary>
    public void AssignNumber(string no)
    {
        if (No.Length > 0) throw new DomainException(Messages.NumberAlreadyAssigned(No));
        No = no;
    }

    public void UpdateHeader(Partner? partner, IssuePurpose? purpose, string? remark)
    {
        EnsureEditable();
        partner?.EnsureUsableFor(Type);
        // An issue says why stock leaves (UX: required); checked before anything changes, so a refusal leaves the draft as it was.
        var newPurpose = Type == DocumentType.Issue ? purpose ?? throw new DomainException(Messages.PurposeRequired) : (IssuePurpose?)null;
        Partner = partner;
        PartnerId = partner?.Id;
        Purpose = newPurpose;
        Remark = remark;
    }

    /// <summary>Replaces every line of a Draft. Removed lines are deleted as orphans by the persistence layer.</summary>
    public void ReplaceLines(IEnumerable<StockDocumentLine> lines)
    {
        EnsureEditable();
        _lines.Clear();
        _lines.AddRange(lines);
    }

    public void AddLines(IEnumerable<StockDocumentLine> lines)
    {
        EnsureEditable();
        _lines.AddRange(lines);
    }

    /// <summary>
    /// Values the lines of an issue that has not posted yet at the current average cost of each product, so the amount an approver sees (and the
    /// approval rules test) is the value of what is leaving. Posting books the real cost over it (BR-05). Receipts carry their entered cost and
    /// are left alone, as is any issue that already posted.
    /// </summary>
    public void EstimateIssueCosts(IReadOnlyDictionary<int, decimal> averageCostByProduct)
    {
        if (Type != DocumentType.Issue || !DocumentStatuses.IsOpen(Status)) return;
        foreach (var line in _lines) line.EstimateIssueCost(averageCostByProduct.GetValueOrDefault(line.ProductId));
    }

    /// <summary>
    /// A receipt saved by someone who may not see or enter cost (Warehouse staff) takes the last known cost of each product in the warehouse, or zero
    /// for a product never stocked there. Receiving at the average cost leaves the average unchanged, so a receipt without a price cannot drag the
    /// valuation of the stock towards zero. Other documents are left alone.
    /// </summary>
    public void ApplyKnownReceiptCosts(IReadOnlyDictionary<int, decimal> averageCostByProduct)
    {
        if (Type != DocumentType.Receipt) return;
        EnsureEditable();
        foreach (var line in _lines.Where(l => l.CostFromAverage)) line.SetKnownCost(averageCostByProduct.GetValueOrDefault(line.ProductId));
    }

    public void EnsureEditable()
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException(Messages.NotEditable(No, Status));
    }

    public void Submit(int userId, bool requiresApproval, DateTime utcNow, bool isLarge = false, bool requiresOverride = false)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException(Messages.OnlyDraftCanSubmit(Status));
        BusinessRules.Ensure(new DocumentHasValidLinesRule(Lines));

        Status = DocumentStatus.Submitted;
        SubmittedBy = userId;
        SubmittedAt = utcNow;
        RequiresApproval = requiresApproval;
        IsLarge = requiresApproval && isLarge;
        RequiresOverride = requiresOverride;
        RejectionReason = null;
        RejectedBy = null;
        RejectedAt = null;
    }

    /// <summary>
    /// Approves a Submitted document. A document that <see cref="RequiresOverride"/> needs the approver's reason for letting stock go
    /// below zero; it is stored trimmed and posting reads it later. A reason given for a document that needs none is ignored.
    /// </summary>
    public void Approve(int userId, bool allowSelfApproval, DateTime utcNow, string? overrideReason = null)
    {
        if (Status != DocumentStatus.Submitted)
            throw new DomainException(Messages.OnlySubmittedCanApprove(Status));
        if (!RequiresApproval)
            throw new DomainException(Messages.ApprovalNotNeeded);
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.SelfApproval));
        if (RequiresOverride)
        {
            BusinessRules.Ensure(new ReasonRequiredRule(overrideReason, Messages.OverrideReasonRequired));
            BusinessRules.Ensure(new ReasonRequiredRule(overrideReason, Messages.ReasonTooShort, Limits.ReasonMinLength));
        }

        Status = DocumentStatus.Approved;
        ApprovedBy = userId;
        ApprovedAt = utcNow;
        NegativeStockOverrideReason = RequiresOverride ? overrideReason!.Trim() : null;
    }

    /// <summary>
    /// Rejection returns the document to Draft and keeps the (trimmed) reason and who returned it. The submit-time flags no longer describe a Draft.
    /// Like approval, it is a review: the submitter may not reject their own document unless self-approval is allowed (the recall is Cancel).
    /// </summary>
    public void Reject(string reason, int userId, bool allowSelfApproval, DateTime utcNow)
    {
        if (Status != DocumentStatus.Submitted)
            throw new DomainException(Messages.OnlySubmittedCanReject(Status));
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.RejectReasonRequired));
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.ReasonTooShort, Limits.ReasonMinLength));
        BusinessRules.Ensure(new SelfApprovalNotAllowedRule(SubmittedBy, userId, allowSelfApproval, Messages.SelfRejection));

        Status = DocumentStatus.Draft;
        RejectionReason = reason.Trim();
        RejectedBy = userId;
        RejectedAt = utcNow;
        SubmittedBy = null;
        SubmittedAt = null;
        RequiresApproval = false;
        IsLarge = false;
        RequiresOverride = false;
    }

    public void Cancel(string? reason)
    {
        if (Status is not (DocumentStatus.Draft or DocumentStatus.Submitted))
            throw new DomainException(Messages.OnlyDraftOrSubmittedCanCancel(Status));
        Status = DocumentStatus.Cancelled;
        CancelReason = reason;
    }

    public void EnsureCanPost()
    {
        if (!CanPost)
            throw new DomainException(Status == DocumentStatus.Submitted
                ? Messages.ApprovalRequiredToPost
                : Messages.CannotPostInStatus(Status));
    }

    public void MarkPosted(int userId, DateTime utcNow)
    {
        EnsureCanPost();
        Status = DocumentStatus.Posted;
        PostedBy = userId;
        PostedAt = utcNow;
    }

    public void MarkReversed(string reason, DateTime utcNow)
    {
        if (Status != DocumentStatus.Posted)
            throw new DomainException(Messages.OnlyPostedCanReverse);
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.ReverseReasonRequired));
        BusinessRules.Ensure(new ReasonRequiredRule(reason, Messages.ReasonTooShort, Limits.ReasonMinLength));

        Status = DocumentStatus.Reversed;
        ReversalReason = reason.Trim();
        ReversedAt = utcNow;
    }
}
