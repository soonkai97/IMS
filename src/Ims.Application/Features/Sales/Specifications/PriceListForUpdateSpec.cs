using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Tracked price list with its items (and the product and unit of each, for the words of the audit), for any command that changes or deletes it.</summary>
public sealed class PriceListForUpdateSpec : Specification<PriceList>
{
    public PriceListForUpdateSpec(Guid publicId) =>
        Query.Where(l => l.PublicId == publicId).Include(l => l.Customer).Include(l => l.Items).ThenInclude(i => i.Product).Include(l => l.Items).ThenInclude(i => i.Uom);
}
