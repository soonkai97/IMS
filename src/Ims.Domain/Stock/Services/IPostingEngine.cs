namespace Ims.Domain;

public interface IPostingEngine
{
    /// <summary>Applies the document's lines to the balances, records ledger entries in the context and marks the document Posted.</summary>
    void Post(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx);

    /// <summary>Applies the opposite movement of every line (BR-02). The caller has already marked the document Reversed.</summary>
    void Reverse(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx);
}
