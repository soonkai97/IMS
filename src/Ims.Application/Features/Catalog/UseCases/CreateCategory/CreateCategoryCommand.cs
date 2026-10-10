namespace Ims.Application.Features.Catalog;

public record CreateCategoryCommand(CategorySaveRequest Data) : ICommand<CategoryDto>;
