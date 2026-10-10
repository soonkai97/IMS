namespace Ims.Domain;

public sealed class IssuePostingStrategy : IPostingStrategy
{
    public DocumentType Type => DocumentType.Issue;

    public void Post(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        if (balance.QtyOnHand < line.BaseQty)
        {
            // BR-03: below-zero stock needs the setting, a supervisor to post and the reason the approver recorded.
            if (!ctx.Settings.AllowNegativeStock)
                throw new InsufficientStockException(balance.ProductId, balance.WarehouseId, balance.QtyOnHand, line.BaseQty);
            if (!ctx.CanOverrideNegative)
                throw new DomainException(Messages.NegativeNeedsSupervisor);
            BusinessRules.Ensure(new ReasonRequiredRule(ctx.OverrideReason, Messages.NegativeNeedsReason));
            ctx.OverrideUsed = true;
        }

        var cost = balance.Issue(line.BaseQty, allowNegative: true);
        line.BookIssueCost(cost);
        ctx.AddEntry(line, -line.BaseQty, cost, LedgerReason.Issue);
    }

    /// <summary>Undo an issue: put the stock back at the cost it left at.</summary>
    public decimal StockChangeOnReverse(StockDocumentLine line) => +line.BaseQty;

    public void Reverse(StockDocumentLine line, StockBalance balance, PostingContext ctx)
    {
        balance.Receive(line.BaseQty, line.UnitCost);
        ctx.AddEntry(line, +line.BaseQty, line.UnitCost, LedgerReason.Reversal);
    }
}
