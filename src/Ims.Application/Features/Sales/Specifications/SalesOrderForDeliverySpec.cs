using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>An order with what a delivery against it needs: its lines (with product, its unit conversions and unit: for the sentences and the base quantity), the warehouse and the customer. Tracked, because a delivery created from it keeps the warehouse and customer as navigations.</summary>
public sealed class SalesOrderForDeliverySpec : Specification<SalesOrder>
{
    public SalesOrderForDeliverySpec(int key) =>
        Query.Where(o => o.Id == key).Include(o => o.Lines).ThenInclude(l => l.Product).ThenInclude(p => p.Conversions).Include(o => o.Lines).ThenInclude(l => l.Uom).Include(o => o.Warehouse).Include(o => o.Customer);
}
