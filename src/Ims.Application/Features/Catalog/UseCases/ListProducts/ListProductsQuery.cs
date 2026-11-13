namespace Ims.Application.Features.Catalog;

public sealed record ListProductsQuery(ProductFilter Filter) : IQuery<PagedResult<ProductDto>>;
