namespace Ims.Domain;

public sealed class ReceiptPostingStrategy : IPostingStrategy
{
    public DocumentType Type => DocumentType.Receipt;

    public void Post(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        balance.Receive(line.BaseQty, line.UnitCost);
        ctx.AddEntry(line, +line.BaseQty, line.UnitCost, LedgerReason.Receipt);
    }

    /// <summary>Undo a receipt: take the same quantity out again at the cost it came in.</summary>
    public decimal StockChangeOnReverse(StockDocumentLine line) => -line.BaseQty;

    public void Reverse(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        balance.Issue(line.BaseQty, ctx.Settings.AllowNegativeStock);
        ctx.AddEntry(line, -line.BaseQty, line.UnitCost, LedgerReason.Reversal);
    }
}
