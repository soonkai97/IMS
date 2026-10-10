using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ListProductsHandler(IReadRepository<Product> products) : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> HandleAsync(ListProductsQuery q, CancellationToken ct)
    {
        var total = await products.CountAsync(new ProductsFilterSpec(q.Filter), ct);
        var items = await products.ListAsync(new ProductsPageSpec(q.Filter), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<ProductDto>(items, total, page, size);
    }
}
