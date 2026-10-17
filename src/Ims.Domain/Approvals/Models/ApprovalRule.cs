namespace Ims.Domain;

/// <summary>BR-06: when a document of a type needs approval, and which tier of approver may give it.</summary>
public class ApprovalRule : PublicEntity
{
    private ApprovalRule() { }

    public DocumentType DocType { get; private set; }
    /// <summary>Document total value at or above this triggers approval. Null = ignore.</summary>
    [VisibleWith(Permissions.CostView)]
    public decimal? AmountThreshold { get; private set; }
    /// <summary>Document total base quantity at or above this triggers approval. Null = ignore.</summary>
    public decimal? QtyThreshold { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// The tier of approver the rule asks for, the only record of "who" (ADR roles-permissions 2.7): a document that fires a Large rule is Large and is reviewed by the
    /// holders of <see cref="Permissions.DocumentApproveLarge"/>, any other by the holders of <see cref="Permissions.DocumentApprove"/>.
    /// </summary>
    public ApproverTier Tier { get; private set; }

    public static ApprovalRule Create(DocumentType docType, decimal? amountThreshold, decimal? qtyThreshold, ApproverTier tier, bool isActive = true)
    {
        var r = new ApprovalRule();
        r.Update(docType, amountThreshold, qtyThreshold, tier, isActive);
        return r;
    }

    public void Update(DocumentType docType, decimal? amountThreshold, decimal? qtyThreshold, ApproverTier tier, bool isActive)
    {
        if (amountThreshold is null && qtyThreshold is null) throw new DomainException(Messages.ThresholdRequired);
        if (amountThreshold < 0 || qtyThreshold < 0) throw new DomainException(Messages.ThresholdNegative);
        if (amountThreshold is { } amount && !Money.IsExact(amount)) throw new DomainException(Messages.AmountTooPrecise);
        if (!Enum.IsDefined(tier)) throw new DomainException(Messages.UnknownApproverTier);

        DocType = docType;
        AmountThreshold = amountThreshold;
        QtyThreshold = qtyThreshold;
        Tier = tier;
        IsActive = isActive;
    }

    public bool Matches(DocumentType type, decimal totalQty, decimal totalAmount) =>
        IsActive && DocType == type &&
        ((AmountThreshold is { } a && totalAmount >= a) || (QtyThreshold is { } q && totalQty >= q));

    /// <summary>The thresholds this document reaches (none when the rule does not match): the plain-words "why" behind an approval.</summary>
    public IEnumerable<ApprovalTrigger> Triggers(DocumentType type, decimal totalQty, decimal totalAmount)
    {
        if (!Matches(type, totalQty, totalAmount)) yield break;
        if (AmountThreshold is { } a && totalAmount >= a) yield return new ApprovalTrigger(ApprovalMetric.Amount, a, totalAmount, Tier);
        if (QtyThreshold is { } q && totalQty >= q) yield return new ApprovalTrigger(ApprovalMetric.Quantity, q, totalQty, Tier);
    }
}
