namespace Ims.Application.Features.Catalog;

public sealed record CreateCategoryCommand(CategorySaveRequest Data) : ICommand<CategoryDto>;
