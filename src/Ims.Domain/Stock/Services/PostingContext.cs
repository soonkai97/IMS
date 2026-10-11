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

    /// <param name="balanceAfter">What the product holds in the warehouse right after this movement: the balance AFTER the strategy applied the line. It is loaded under the (warehouse, product) lock and saved in the same transaction as the entry, so no other posting can slip in between.</param>
    public void AddEntry(StockDocumentLine line, decimal qtyDelta, decimal balanceAfter, decimal unitCost, LedgerReason reason) =>
        Entries.Add(StockLedgerEntry.Create(Document, line.ProductId, qtyDelta, balanceAfter, unitCost, reason, UserId, Now));
}
