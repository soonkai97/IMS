using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Lookup lists are small by nature, but they are capped anyway so memory and response size can never grow without bound.</summary>
public sealed class CategoriesLookupSpec : Specification<Category>
{
    public CategoriesLookupSpec() => Query.AsNoTracking().OrderBy(c => c.Name).Take(Limits.MaxLookupRows);
}
