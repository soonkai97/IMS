using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Tracked product with its barcodes and conversions, for editing, named by the public id a client sent.</summary>
public sealed class ProductForEditSpec : Specification<Product>
{
    public ProductForEditSpec(Guid publicId) => Query.Where(p => p.PublicId == publicId).Include(p => p.Barcodes).Include(p => p.Conversions);
}
