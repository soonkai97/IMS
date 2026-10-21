using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public sealed class ProductsBySkusSpec : Specification<Product>
{
    public ProductsBySkusSpec(IReadOnlyCollection<string> skus) => Query.Where(p => skus.Contains(p.Sku)).Include(p => p.Barcodes).Include(p => p.Conversions);
}
