using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>One price list for reading, with its customer and the product and unit of each item.</summary>
public sealed class PriceListWithItemsSpec : Specification<PriceList>
{
    public PriceListWithItemsSpec(Guid publicId) =>
        Query.Where(l => l.PublicId == publicId).Include(l => l.Customer).Include(l => l.Items).ThenInclude(i => i.Product).Include(l => l.Items).ThenInclude(i => i.Uom).AsNoTracking();
}
