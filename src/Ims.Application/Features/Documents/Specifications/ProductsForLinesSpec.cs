using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>The products the lines of a document name by public id, with their conversions (one query for the whole document).</summary>
public sealed class ProductsForLinesSpec : Specification<Product>
{
    public ProductsForLinesSpec(IReadOnlyCollection<Guid> publicIds) =>
        Query.Where(p => publicIds.Contains(p.PublicId)).Include(p => p.Conversions).AsNoTracking();
}
