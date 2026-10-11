using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IReceiptCostDefaults
{
    /// <summary>
    /// A receipt line whose cost is not typed (<c>CostFromAverage</c>: saved by a caller who may not see or enter cost, no <c>cost.view</c> - the Warehouse staff - or
    /// kept at the average when a draft that already was is saved again) takes the last known (average) cost of each product in the warehouse, or zero for a
    /// product never stocked there, whatever cost the request carried. One balance query for the whole document. Lines with a typed cost, and issues, are left alone.
    /// </summary>
    Task ApplyAsync(StockDocument document, CancellationToken ct);
}
