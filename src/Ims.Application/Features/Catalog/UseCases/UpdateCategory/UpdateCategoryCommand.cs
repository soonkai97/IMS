namespace Ims.Application.Features.Catalog;

public record UpdateCategoryCommand(Guid Id, CategorySaveRequest Data) : ICommand<CategoryDto>;
