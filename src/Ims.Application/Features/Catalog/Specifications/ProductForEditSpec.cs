using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Tracked product with its barcodes and conversions, for editing.</summary>
public sealed class ProductForEditSpec : Specification<Product>
{
    public ProductForEditSpec(int id) => Query.Where(p => p.Id == id).Include(p => p.Barcodes).Include(p => p.Conversions);
}
