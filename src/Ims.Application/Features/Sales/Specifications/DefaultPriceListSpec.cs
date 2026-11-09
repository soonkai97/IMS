using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The default list, when it is not the list <c>exceptId</c> (0 for a new list).</summary>
public sealed class DefaultPriceListSpec : Specification<PriceList>
{
    public DefaultPriceListSpec(int exceptId) => Query.Where(l => l.IsDefault && l.Id != exceptId).AsNoTracking();
}
