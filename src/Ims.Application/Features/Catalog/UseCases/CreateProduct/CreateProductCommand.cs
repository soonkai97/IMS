namespace Ims.Application.Features.Catalog;

public record CreateProductCommand(ProductSaveRequest Data) : ICommand<ProductDto>;
