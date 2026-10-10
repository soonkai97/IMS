using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class CategoryNameTakenSpec : Specification<Category>
{
    public CategoryNameTakenSpec(string name, int exceptId) => Query.Where(c => c.Name == name && c.Id != exceptId).AsNoTracking();
}
