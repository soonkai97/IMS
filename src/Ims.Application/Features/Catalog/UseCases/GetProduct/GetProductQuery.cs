namespace Ims.Application.Features.Catalog;

public record GetProductQuery(Guid Id) : IQuery<ProductDto>;
