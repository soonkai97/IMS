using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The domain knows a stock conflict by ids ("product 1 in warehouse 1"). This step, which runs after the failed request has finished, describes
/// it with the SKU, the warehouse name and the unit, so the message that reaches a person (and the log) is one they can act on (NV-06, PS-01). It also gives the exception the product's public id, the only product id that leaves the system.
/// A product or warehouse that cannot be named (soft-deleted) is still said by public id: no integer key is ever in the message.
/// </summary>
public sealed class ShortageNamingBehavior(IReadRepository<Product> products, IReadRepository<Warehouse> warehouses, IIdMap<Product> productIds, IIdMap<Warehouse> warehouseIds) : IPipelineBehavior
{
    public async Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (InsufficientStockException shortage) when (shortage.Sku is null)
        {
            var label = (await products.ListAsync(new ProductLabelsSpec([shortage.ProductId]), ct)).FirstOrDefault();
            var warehouse = await warehouses.GetByIdAsync(shortage.WarehouseId, ct);
            if (label is not null && warehouse is not null) throw new InsufficientStockException(shortage, label.PublicId, label.Sku, warehouse.Name, label.UomCode);

            // ToPublicAsync also names soft-deleted rows. An id with no row at all cannot happen (foreign keys); it would read as the empty id.
            var productId = (await productIds.ToPublicAsync([shortage.ProductId], ct)).GetValueOrDefault(shortage.ProductId);
            var warehouseId = (await warehouseIds.ToPublicAsync([shortage.WarehouseId], ct)).GetValueOrDefault(shortage.WarehouseId);
            throw new InsufficientStockException(shortage, productId, warehouseId);
        }
    }
}
