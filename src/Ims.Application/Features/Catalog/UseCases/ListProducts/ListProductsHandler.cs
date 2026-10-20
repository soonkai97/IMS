using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ListProductsHandler(IReadRepository<Product> products, IReadRepository<ProductBarcode> barcodes, IIdMap<Category> categories, IModuleSwitches modules) : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> HandleAsync(ListProductsQuery q, CancellationToken ct)
    {
        var search = await ProductSearch.ResolveAsync(barcodes, q.Filter.Search, ct);
        var category = await categories.ToInternalOrNoRowAsync(q.Filter.CategoryId, ct);
        var total = await products.CountAsync(new ProductsFilterSpec(q.Filter, category, search), ct);
        var items = await modules.ShapeAsync(await products.ListAsync(new ProductsPageSpec(q.Filter, category, search), ct), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<ProductDto>(items, total, page, size);
    }
}
