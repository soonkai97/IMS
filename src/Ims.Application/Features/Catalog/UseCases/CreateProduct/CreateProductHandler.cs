using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class CreateProductHandler(IProductWriter writer, IReadRepository<Product> products) : ICommandHandler<CreateProductCommand, ProductDto>
{
    public async Task<ProductDto> HandleAsync(CreateProductCommand cmd, CancellationToken ct)
    {
        var id = await writer.SaveAsync(null, cmd.Data, ct);
        return await products.FirstOrDefaultAsync(new ProductDtoSpec(id), ct) ?? throw new NotFoundException(Messages.ProductNotFound);
    }
}
