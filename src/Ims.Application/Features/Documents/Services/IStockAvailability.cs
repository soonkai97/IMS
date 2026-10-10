using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IStockAvailability
{
    /// <exception cref="InsufficientStockException">An issue line needs more than is on hand.</exception>
    Task EnsureAvailableAsync(StockDocument document, CancellationToken ct);
}
