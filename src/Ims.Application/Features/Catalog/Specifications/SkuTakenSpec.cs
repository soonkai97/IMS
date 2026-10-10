using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class SkuTakenSpec : Specification<Product>
{
    public SkuTakenSpec(string sku, int exceptId) => Query.Where(p => p.Sku == sku && p.Id != exceptId).AsNoTracking();
}
