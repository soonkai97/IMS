namespace Ims.Domain;

/// <summary>BR-06: when a document of a type needs approval, and who may give it.</summary>
public class ApprovalRule : PublicEntity
{
    private ApprovalRule() { }

    public DocumentType DocType { get; private set; }
    /// <summary>Document total value at or above this triggers approval. Null = ignore.</summary>
    [VisibleWith(Permissions.CostView)]
    public decimal? AmountThreshold { get; private set; }
    /// <summary>Document total base quantity at or above this triggers approval. Null = ignore.</summary>
    public decimal? QtyThreshold { get; private set; }
    public string RoleRequired { get; private set; } = Roles.Supervisor;
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// The tier of approver the rule asks for. Stored since RP-2, written with <see cref="RoleRequired"/> on every save and derived from it until RP-4b makes the
    /// tier the source: Owner and Admin are Large, anything else Standard, exactly the old "a rule above a supervisor" test, so which documents are large does not change.
    /// The migration gave the rules saved before it the same value.
    /// </summary>
    public ApproverTier Tier { get; private set; }

    public static ApprovalRule Create(DocumentType docType, decimal? amountThreshold, decimal? qtyThreshold, string roleRequired, bool isActive = true)
    {
        var r = new ApprovalRule();
        r.Update(docType, amountThreshold, qtyThreshold, roleRequired, isActive);
        return r;
    }

    public void Update(DocumentType docType, decimal? amountThreshold, decimal? qtyThreshold, string roleRequired, bool isActive)
    {
        if (amountThreshold is null && qtyThreshold is null) throw new DomainException(Messages.ThresholdRequired);
        if (amountThreshold < 0 || qtyThreshold < 0) throw new DomainException(Messages.ThresholdNegative);
        if (amountThreshold is { } amount && !Money.IsExact(amount)) throw new DomainException(Messages.AmountTooPrecise);
        if (!Roles.All.Contains(roleRequired)) throw new DomainException(Messages.UnknownRole);

        DocType = docType;
        AmountThreshold = amountThreshold;
        QtyThreshold = qtyThreshold;
        RoleRequired = roleRequired;
        Tier = roleRequired is Roles.Owner or Roles.Admin ? ApproverTier.Large : ApproverTier.Standard;
        IsActive = isActive;
    }

    public bool Matches(DocumentType type, decimal totalQty, decimal totalAmount) =>
        IsActive && DocType == type &&
        ((AmountThreshold is { } a && totalAmount >= a) || (QtyThreshold is { } q && totalQty >= q));

    /// <summary>The thresholds this document reaches (none when the rule does not match): the plain-words "why" behind an approval.</summary>
    public IEnumerable<ApprovalTrigger> Triggers(DocumentType type, decimal totalQty, decimal totalAmount)
    {
        if (!Matches(type, totalQty, totalAmount)) yield break;
        if (AmountThreshold is { } a && totalAmount >= a) yield return new ApprovalTrigger(ApprovalMetric.Amount, a, totalAmount, RoleRequired);
        if (QtyThreshold is { } q && totalQty >= q) yield return new ApprovalTrigger(ApprovalMetric.Quantity, q, totalQty, RoleRequired);
    }
}
