using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The posted quantities of a sales order per order line, summed by the database (the answer is a few rows however many deliveries there are). It will run under the order's lock when a delivery posts (PL3.7).</summary>
public interface IDeliveredQuantityReader
{
    Task<IReadOnlyDictionary<int, DeliveredQuantity>> PostedByLineAsync(int orderKey, CancellationToken ct);
}
