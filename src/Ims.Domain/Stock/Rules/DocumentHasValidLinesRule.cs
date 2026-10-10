namespace Ims.Domain;

/// <summary>A document can only be submitted with at least one line, and every line must move a positive quantity.</summary>
public sealed class DocumentHasValidLinesRule(IReadOnlyCollection<StockDocumentLine> lines) : IBusinessRule
{
    public string Message => lines.Count == 0 ? Messages.NoLines : Messages.LineQtyPositive;
    public bool IsBroken() => lines.Count == 0 || lines.Any(l => l.Qty <= 0);
}
