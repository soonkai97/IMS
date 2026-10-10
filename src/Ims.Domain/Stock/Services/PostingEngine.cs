namespace Ims.Domain;

/// <summary>Picks the strategy for the document type and applies it to every line, then moves the document to its next state.</summary>
public sealed class PostingEngine(IEnumerable<IPostingStrategy> strategies) : IPostingEngine
{
    private readonly Dictionary<DocumentType, IPostingStrategy> _byType = strategies.ToDictionary(s => s.Type);

    public void Post(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx)
    {
        // Refuse before any balance is touched: a document that cannot post must leave the stock exactly as it was.
        document.EnsureCanPost();
        var strategy = StrategyFor(document.Type);
        // Lines are applied in ProductId order so balance rows are always updated in the same global order (no lock cycles).
        foreach (var line in OrderedLines(document)) strategy.Post(line, balances[line.ProductId], ctx);
        document.MarkPosted(ctx.UserId, ctx.Now);
    }

    public IReadOnlyDictionary<int, decimal> FindReversalDeficits(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, SystemSettings settings)
    {
        var deficits = new SortedDictionary<int, decimal>();
        if (settings.AllowNegativeStock) return deficits;

        var strategy = StrategyFor(document.Type);
        foreach (var product in document.Lines.GroupBy(l => l.ProductId).OrderBy(g => g.Key))
        {
            var change = product.Sum(strategy.StockChangeOnReverse);
            var after = balances[product.Key].QtyOnHand + change;
            if (change < 0 && after < 0) deficits[product.Key] = after;
        }
        return deficits;
    }

    public void Reverse(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx)
    {
        var strategy = StrategyFor(document.Type);
        foreach (var line in OrderedLines(document)) strategy.Reverse(line, balances[line.ProductId], ctx);
    }

    private static IEnumerable<StockDocumentLine> OrderedLines(StockDocument document) =>
        document.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Id);

    private IPostingStrategy StrategyFor(DocumentType type) =>
        _byType.TryGetValue(type, out var s) ? s : throw new DomainException(Messages.PostingNotSupported(type));
}
