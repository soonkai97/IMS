using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>The lines of the Posted receipts linked to one order, as (order line number, quantity, base quantity): a seek on the filtered index of <c>PurchaseOrderId</c>, then the lines of those few documents.</summary>
public sealed class PostedReceiptQuantitiesSpec : Specification<StockDocumentLine, ReceiptQuantity>
{
    public PostedReceiptQuantitiesSpec(int orderKey) =>
        Query.AsNoTracking()
            .Where(l => l.Document.PurchaseOrderId == orderKey && l.Document.Status == DocumentStatus.Posted && l.PurchaseOrderLineNo != null)
            .Select(l => new ReceiptQuantity(l.PurchaseOrderLineNo!.Value, l.Qty, l.BaseQty));
}
