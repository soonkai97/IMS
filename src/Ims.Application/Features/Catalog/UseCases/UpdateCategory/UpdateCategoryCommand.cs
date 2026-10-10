namespace Ims.Application.Features.Catalog;

public record UpdateCategoryCommand(int Id, CategorySaveRequest Data) : ICommand<CategoryDto>;
