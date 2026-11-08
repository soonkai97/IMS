using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The deliveries created from one order, in the order they were made, whatever their state.</summary>
public sealed class DeliveriesOfOrderSpec : Specification<StockDocument, SalesOrderDeliveryDto>
{
    public DeliveriesOfOrderSpec(int orderKey) =>
        Query.AsNoTracking().Where(d => d.SalesOrderId == orderKey).OrderBy(d => d.Id)
            .Select(d => new SalesOrderDeliveryDto(d.PublicId, d.No, d.Status, d.CreatedAt, d.PostedAt));
}
