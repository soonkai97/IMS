using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class ProductDtoSpec : Specification<Product, ProductDto>
{
    public ProductDtoSpec(int id) => Query.AsNoTracking().Where(p => p.Id == id).Select(ProductProjection.Expression);
}
