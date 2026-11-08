using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The integer key of an order and its warehouse, from its public id: asked first by the writes that must take the order's lock before they read it. The warehouse comes with it so the caller's scope is checked BEFORE the lock is taken.</summary>
public sealed class SalesOrderKeySpec : Specification<SalesOrder, SalesOrderKey>
{
    public SalesOrderKeySpec(Guid publicId) => Query.AsNoTracking().Where(o => o.PublicId == publicId).Select(o => new SalesOrderKey(o.Id, o.WarehouseId));
}
