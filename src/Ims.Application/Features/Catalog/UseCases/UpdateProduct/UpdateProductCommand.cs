namespace Ims.Application.Features.Catalog;

public record UpdateProductCommand(Guid Id, ProductSaveRequest Data) : ICommand<ProductDto>;
