namespace Ims.Domain;

/// <summary>Inputs and outputs of one posting or reversal run.</summary>
public class PostingContext(StockDocument document, SystemSettings settings, int userId, bool canOverrideNegative, string? overrideReason, DateTime now)
{
    public StockDocument Document { get; } = document;
    public SystemSettings Settings { get; } = settings;
    public int UserId { get; } = userId;
    public bool CanOverrideNegative { get; } = canOverrideNegative;
    public string? OverrideReason { get; } = overrideReason;
    public DateTime Now { get; } = now;

    /// <summary>Ledger entries produced by the run. The caller persists them.</summary>
    public List<StockLedgerEntry> Entries { get; } = [];
    public bool OverrideUsed { get; set; }

    public void AddEntry(StockDocumentLine line, decimal qtyDelta, decimal unitCost, LedgerReason reason) =>
        Entries.Add(StockLedgerEntry.Create(Document, line.ProductId, qtyDelta, unitCost, reason, UserId, Now));
}
