namespace Ims.Domain;

public sealed class ReceiptPostingStrategy : IPostingStrategy
{
    public DocumentType Type => DocumentType.Receipt;

    public void Post(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        var averageBefore = balance.AvgCost;
        balance.Receive(line.BaseQty, line.UnitCost);
        // Remember what this line did to the average, so reversing the receipt can put it back (StockBalance.Unreceive).
        line.RecordAverageChange(averageBefore, balance.AvgCost);
        ctx.AddEntry(line, +line.BaseQty, line.UnitCost, LedgerReason.Receipt);
    }

    /// <summary>Undo a receipt: take the same quantity out again at the cost it came in.</summary>
    public decimal StockChangeOnReverse(StockDocumentLine line) => -line.BaseQty;

    /// <summary>The quantity leaves at the cost it came in at, and the average cost goes back with it (see <see cref="StockBalance.Unreceive"/>).</summary>
    public void Reverse(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        balance.Unreceive(line.BaseQty, line.UnitCost, line.PostedAverage, ctx.Settings.AllowNegativeStock);
        ctx.AddEntry(line, -line.BaseQty, line.UnitCost, LedgerReason.Reversal);
    }
}
