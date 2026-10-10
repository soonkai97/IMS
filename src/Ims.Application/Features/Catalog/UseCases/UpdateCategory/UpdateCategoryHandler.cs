using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class UpdateCategoryHandler(IRepository<Category> categories, IUnitOfWork uow) : ICommandHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> HandleAsync(UpdateCategoryCommand cmd, CancellationToken ct)
    {
        var name = cmd.Data.Name.Trim();
        var c = await categories.GetByIdAsync(cmd.Id, ct) ?? throw new NotFoundException(Messages.CategoryNotFound);
        if (await categories.AnyAsync(new CategoryNameTakenSpec(name, cmd.Id), ct)) throw new ConflictException(Messages.CategoryExists);
        c.Rename(name);
        await uow.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name);
    }
}
