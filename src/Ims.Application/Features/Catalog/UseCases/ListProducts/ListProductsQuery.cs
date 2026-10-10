namespace Ims.Application.Features.Catalog;

public record ListProductsQuery(ProductFilter Filter) : IQuery<PagedResult<ProductDto>>;
