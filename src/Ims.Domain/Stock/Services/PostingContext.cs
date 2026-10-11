namespace Ims.Domain;

/// <summary>Inputs and outputs of one posting or reversal run.</summary>
public class PostingContext(StockDocument document, SystemSettings settings, int userId, bool canOverrideNegative, DateTime now)
{
    public StockDocument Document { get; } = document;
    public SystemSettings Settings { get; } = settings;
    public int UserId { get; } = userId;
    public bool CanOverrideNegative { get; } = canOverrideNegative;
    /// <summary>Why the approver allowed stock to go below zero: recorded on the document at approval, never taken from the poster.</summary>
    public string? OverrideReason => Document.NegativeStockOverrideReason;
    public DateTime Now { get; } = now;

    /// <summary>Ledger entries produced by the run. The caller persists them.</summary>
    public List<StockLedgerEntry> Entries { get; } = [];
    public bool OverrideUsed { get; set; }

    public void AddEntry(StockDocumentLine line, decimal qtyDelta, decimal unitCost, LedgerReason reason) =>
        Entries.Add(StockLedgerEntry.Create(Document, line.ProductId, qtyDelta, unitCost, reason, UserId, Now));

    /// <summary>
    /// Writes onto every entry of the run what its product held right after it (<see cref="StockLedgerEntry.BalanceAfter"/>). Call it once the run is complete, with
    /// the balances the run changed: the last entry of a product ends at the balance's quantity, and each earlier entry is the later balance minus the movements
    /// after it. The balances are loaded under the (warehouse, product) lock and saved in the same transaction as the entries, so the number cannot be overtaken by
    /// another posting.
    /// </summary>
    public void StampBalances(IReadOnlyDictionary<int, StockBalance> balances)
    {
        foreach (var product in Entries.GroupBy(e => e.ProductId))
        {
            var running = balances[product.Key].QtyOnHand;
            foreach (var entry in product.Reverse())
            {
                entry.StampBalanceAfter(running);
                running -= entry.QtyDelta;
            }
        }
    }
}
