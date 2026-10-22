using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class GetProductHandler(IReadRepository<Product> products) : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<ProductDto> HandleAsync(GetProductQuery q, CancellationToken ct) =>
        await products.FirstOrDefaultAsync(new ProductDtoSpec(q.Id), ct) ?? throw new NotFoundException(Messages.ProductNotFound);
}
