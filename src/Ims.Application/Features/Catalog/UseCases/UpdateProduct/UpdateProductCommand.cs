namespace Ims.Application.Features.Catalog;

public record UpdateProductCommand(int Id, ProductSaveRequest Data) : ICommand<ProductDto>;
