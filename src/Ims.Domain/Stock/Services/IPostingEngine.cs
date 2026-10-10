namespace Ims.Domain;

public interface IPostingEngine
{
    /// <summary>Applies the document's lines to the balances, records ledger entries in the context and marks the document Posted.</summary>
    void Post(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx);

    /// <summary>
    /// Every product a reversal would push below zero, with the balance it would end at (ascending product id; empty when negative stock is
    /// allowed or nothing falls short). Checked for the whole document before any balance is touched, so the answer names all of them.
    /// </summary>
    IReadOnlyDictionary<int, decimal> FindReversalDeficits(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, SystemSettings settings);

    /// <summary>Applies the opposite movement of every line (BR-02). The caller has already marked the document Reversed.</summary>
    void Reverse(StockDocument document, IReadOnlyDictionary<int, StockBalance> balances, PostingContext ctx);
}
