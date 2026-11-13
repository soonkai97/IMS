namespace Ims.Application.Features.Catalog;

public sealed record UpdateProductCommand(Guid Id, ProductSaveRequest Data) : ICommand<ProductDto>;
