using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IStockAvailability
{
    /// <exception cref="InsufficientStockException">An issue line needs more than is on hand.</exception>
    Task EnsureAvailableAsync(StockDocument document, CancellationToken ct);

    /// <summary>True when any product on the document needs more than is on hand in its warehouse.</summary>
    Task<bool> IsShortAsync(StockDocument document, CancellationToken ct);

    /// <summary>On hand in base units per product (one query for all of them); a product with no balance row is missing, meaning zero.</summary>
    Task<IReadOnlyDictionary<int, decimal>> OnHandAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct);
}
