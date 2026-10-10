namespace Ims.Application.Features.Catalog;

public record FindProductByCodeQuery(string Code) : IQuery<ProductDto?>;
