using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductDtoSpec : Specification<Product, ProductDto>
{
    /// <summary>By the integer key, for a product the handler has just saved.</summary>
    public ProductDtoSpec(int id) => Query.AsNoTracking().Where(p => p.Id == id).Select(ProductProjection.Expression);

    /// <summary>By the public id a client sent.</summary>
    public ProductDtoSpec(Guid publicId) => Query.AsNoTracking().Where(p => p.PublicId == publicId).Select(ProductProjection.Expression);
}
