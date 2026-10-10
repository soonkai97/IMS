namespace Ims.Application.Features.Catalog;

public record GetProductQuery(int Id) : IQuery<ProductDto>;
