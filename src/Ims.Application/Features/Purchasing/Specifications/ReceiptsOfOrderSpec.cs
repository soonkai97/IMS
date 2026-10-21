using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>The receipts created from one order, in the order they were made, whatever their state.</summary>
public sealed class ReceiptsOfOrderSpec : Specification<StockDocument, PurchaseOrderReceiptDto>
{
    public ReceiptsOfOrderSpec(int orderKey) =>
        Query.AsNoTracking().Where(d => d.PurchaseOrderId == orderKey).OrderBy(d => d.Id)
            .Select(d => new PurchaseOrderReceiptDto(d.PublicId, d.No, d.Status, d.CreatedAt, d.PostedAt));
}
