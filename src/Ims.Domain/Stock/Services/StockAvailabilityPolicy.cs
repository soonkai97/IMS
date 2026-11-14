namespace Ims.Domain;

/// <summary>OUT-02: by default an issue cannot be submitted when stock is insufficient.</summary>
public static class StockAvailabilityPolicy
{
    /// <summary>Only an issue takes stock out, so only an issue is checked: a receipt is never short.</summary>
    public static bool Covers(StockDocument document) => document.Type == DocumentType.Issue;

    /// <summary>
    /// What a submit does about short stock: nothing when the document is not covered or nothing is short; refused while negative stock is not allowed; otherwise true, the document must carry an override
    /// that its approver answers with a reason (BR-03).
    /// </summary>
    /// <param name="onHandByProduct">Quantity on hand per product in the document's warehouse; a missing product means zero.</param>
    /// <exception cref="InsufficientStockException">The document is covered, short, and negative stock is not allowed.</exception>
    public static bool RequiresOverride(StockDocument document, IReadOnlyDictionary<int, decimal> onHandByProduct, bool allowNegativeStock)
    {
        if (!Covers(document)) return false;
        if (allowNegativeStock) return IsShort(document, onHandByProduct);
        EnsureAvailable(document, onHandByProduct);
        return false;
    }

    /// <summary>A document with an override always goes to an approver, who gives the reason, whatever the value rules say.</summary>
    public static bool RequiresApproval(bool requiredByRules, bool requiresOverride) => requiredByRules || requiresOverride;

    /// <param name="onHandByProduct">Quantity on hand per product in the document's warehouse; a missing product means zero.</param>
    /// <exception cref="InsufficientStockException">A product needs more than is on hand (lines of one product are added together).</exception>
    public static void EnsureAvailable(StockDocument document, IReadOnlyDictionary<int, decimal> onHandByProduct)
    {
        foreach (var (productId, qty) in NeedByProduct(document))
        {
            var have = onHandByProduct.GetValueOrDefault(productId);
            if (have < qty) throw new InsufficientStockException(productId, document.WarehouseId, have, qty);
        }
    }

    /// <summary>True when any product needs more than is on hand (the case an override must cover when negative stock is allowed).</summary>
    public static bool IsShort(StockDocument document, IReadOnlyDictionary<int, decimal> onHandByProduct) =>
        NeedByProduct(document).Any(need => onHandByProduct.GetValueOrDefault(need.Key) < need.Value);

    private static SortedDictionary<int, decimal> NeedByProduct(StockDocument document) =>
        new(document.Lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.BaseQty)));
}
