using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class CreateCategoryHandler(IRepository<Category> categories, IUnitOfWork uow) : ICommandHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> HandleAsync(CreateCategoryCommand cmd, CancellationToken ct)
    {
        var name = cmd.Data.Name.Trim();
        if (await categories.AnyAsync(new CategoryNameTakenSpec(name, 0), ct)) throw new ConflictException(Messages.CategoryExists);
        var c = Category.Create(name);
        categories.Add(c);
        await uow.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name);
    }
}
