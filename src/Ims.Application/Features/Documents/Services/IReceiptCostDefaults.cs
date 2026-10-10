using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IReceiptCostDefaults
{
    /// <summary>
    /// A receipt saved by a caller who may not see or enter cost (no <c>cost.view</c>: the Warehouse staff) takes the last known (average) cost of each
    /// product in the warehouse, or zero for a product never stocked there, whatever cost the request carried. One balance query for the whole
    /// document. Callers who may enter cost, and issues, are left alone.
    /// </summary>
    Task ApplyAsync(StockDocument document, CancellationToken ct);
}
