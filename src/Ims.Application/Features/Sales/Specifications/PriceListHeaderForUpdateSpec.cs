using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Tracked price list with its customer and no items, for a change of the header only.</summary>
public sealed class PriceListHeaderForUpdateSpec : Specification<PriceList>
{
    public PriceListHeaderForUpdateSpec(Guid publicId) => Query.Where(l => l.PublicId == publicId).Include(l => l.Customer);
}
