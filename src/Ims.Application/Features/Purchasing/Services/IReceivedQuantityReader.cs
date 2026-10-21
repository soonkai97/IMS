using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>The posted quantities of an order per order line, summed by the database (the answer is a few rows however many receipts there are). It runs under the order's lock when a receipt posts.</summary>
public interface IReceivedQuantityReader
{
    Task<IReadOnlyDictionary<int, ReceivedQuantity>> PostedByLineAsync(int orderKey, CancellationToken ct);
}
