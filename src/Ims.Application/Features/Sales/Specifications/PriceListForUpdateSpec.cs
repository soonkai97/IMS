using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Tracked price list with its customer and its items, for any command that changes or deletes it. The product and unit of each item are not loaded: the audit subscriber names them in one lookup of its own.</summary>
public sealed class PriceListForUpdateSpec : Specification<PriceList>
{
    public PriceListForUpdateSpec(Guid publicId) =>
        Query.Where(l => l.PublicId == publicId).Include(l => l.Customer).Include(l => l.Items);
}
