using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The domain knows a stock conflict by ids ("product 1 in warehouse 1"). This step, which runs after the failed request has finished, describes
/// it with the SKU, the warehouse name and the unit, so the message that reaches a person (and the log) is one they can act on (NV-06, PS-01).
/// </summary>
public sealed class ShortageNamingBehavior(IReadRepository<Product> products, IReadRepository<Warehouse> warehouses) : IPipelineBehavior
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
            if (label is null || warehouse is null) throw;
            throw new InsufficientStockException(shortage, label.Sku, warehouse.Name, label.UomCode);
        }
    }
}
