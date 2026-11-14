using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IStockAvailability
{
    /// <summary>Reads the on-hand quantities of the document's products (one query) and asks <see cref="StockAvailabilityPolicy.RequiresOverride"/>: true when the document is short and negative stock is allowed.</summary>
    /// <exception cref="InsufficientStockException">An issue line needs more than is on hand and negative stock is not allowed.</exception>
    Task<bool> RequiresOverrideAsync(StockDocument document, bool allowNegativeStock, CancellationToken ct);

    /// <summary>On hand in base units per product (one query for all of them); a product with no balance row is missing, meaning zero.</summary>
    Task<IReadOnlyDictionary<int, decimal>> OnHandAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct);
}
