namespace Ims.Application.Features.Catalog;

public sealed record CreateProductCommand(ProductSaveRequest Data) : ICommand<ProductDto>;
