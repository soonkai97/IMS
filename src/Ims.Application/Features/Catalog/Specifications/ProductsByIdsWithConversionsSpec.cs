using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductsByIdsWithConversionsSpec : Specification<Product>
{
    public ProductsByIdsWithConversionsSpec(IReadOnlyCollection<int> ids) =>
        Query.Where(p => ids.Contains(p.Id)).Include(p => p.Conversions).AsNoTracking();
}
