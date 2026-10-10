using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ListCategoriesHandler(IReadRepository<Category> categories) : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(ListCategoriesQuery q, CancellationToken ct) =>
        (await categories.ListAsync(new CategoriesLookupSpec(), ct)).Select(c => new CategoryDto(c.Id, c.Name)).ToList();
}
