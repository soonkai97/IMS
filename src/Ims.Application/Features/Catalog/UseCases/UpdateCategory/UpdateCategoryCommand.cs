namespace Ims.Application.Features.Catalog;

public sealed record UpdateCategoryCommand(Guid Id, CategorySaveRequest Data) : ICommand<CategoryDto>;
