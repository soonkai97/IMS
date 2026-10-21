using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>The units a product was ordered in, on every purchase order that is not cancelled (Draft, Submitted, Approved or Closed), one row per line. Read by the product index of the order lines.</summary>
public sealed class OrderedUnitsOfProductSpec : Specification<PurchaseOrderLine, int>
{
    public OrderedUnitsOfProductSpec(int productId, IReadOnlyCollection<int> unitIds) =>
        Query.Where(l => l.ProductId == productId && unitIds.Contains(l.UomId) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled).Select(l => l.UomId);
}
