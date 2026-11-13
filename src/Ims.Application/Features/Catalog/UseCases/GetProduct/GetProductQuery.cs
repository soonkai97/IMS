namespace Ims.Application.Features.Catalog;

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;
