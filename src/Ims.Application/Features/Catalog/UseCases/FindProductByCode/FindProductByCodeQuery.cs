namespace Ims.Application.Features.Catalog;

public sealed record FindProductByCodeQuery(string Code) : IQuery<ProductLookupResult>;
