namespace Ims.Domain;

/// <summary>OUT-02: by default an issue cannot be submitted when stock is insufficient.</summary>
public static class StockAvailabilityPolicy
{
    /// <param name="onHandByProduct">Quantity on hand per product in the document's warehouse; a missing product means zero.</param>
    /// <exception cref="InsufficientStockException">A product needs more than is on hand (lines of one product are added together).</exception>
    public static void EnsureAvailable(StockDocument document, IReadOnlyDictionary<int, decimal> onHandByProduct)
    {
        var need = document.Lines.GroupBy(l => l.ProductId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(l => l.BaseQty));
        foreach (var (productId, qty) in need)
        {
            var have = onHandByProduct.GetValueOrDefault(productId);
            if (have < qty) throw new InsufficientStockException(productId, document.WarehouseId, have, qty);
        }
    }
}
