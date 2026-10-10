namespace Ims.Domain;

/// <summary>A reversal would take stock below zero for one or more products (negative stock is off). Carries every offending product, not just the first.</summary>
public class ReversalBlockedException(string documentNo, IReadOnlyList<ReversalShortage> shortages)
    : DomainException(Messages.ReversalBlocked(documentNo, shortages))
{
    public string DocumentNo { get; } = documentNo;
    public IReadOnlyList<ReversalShortage> Shortages { get; } = shortages;
}
