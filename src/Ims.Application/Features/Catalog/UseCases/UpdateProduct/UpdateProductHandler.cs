using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class UpdateProductHandler(IProductWriter writer, IReadRepository<Product> products) : ICommandHandler<UpdateProductCommand, ProductDto>
{
    public async Task<ProductDto> HandleAsync(UpdateProductCommand cmd, CancellationToken ct)
    {
        await writer.SaveAsync(cmd.Id, cmd.Data, ct);
        return await products.FirstOrDefaultAsync(new ProductDtoSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.ProductNotFound);
    }
}
