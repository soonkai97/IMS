using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>An order with what a receipt against it needs: its lines (with product, its unit conversions and unit: for the sentences, the price and the base quantity), the warehouse and the supplier. Tracked, because a receipt created from it keeps the warehouse and supplier as navigations.</summary>
public sealed class PurchaseOrderForReceiptSpec : Specification<PurchaseOrder>
{
    public PurchaseOrderForReceiptSpec(int key) =>
        Query.Where(o => o.Id == key).Include(o => o.Lines).ThenInclude(l => l.Product).ThenInclude(p => p.Conversions).Include(o => o.Lines).ThenInclude(l => l.Uom).Include(o => o.Warehouse).Include(o => o.Supplier);
}
