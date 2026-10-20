using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class GetProductHandler(IReadRepository<Product> products, IModuleSwitches modules) : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductQuery q, CancellationToken ct) =>
        await modules.ShapeAsync(await products.FirstOrDefaultAsync(new ProductDtoSpec(q.Id), ct) ?? throw new NotFoundException(Messages.ProductNotFound), ct);
}
