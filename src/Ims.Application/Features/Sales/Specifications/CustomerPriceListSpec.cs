using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The list of a customer (an integer key), when it is not the list <c>exceptId</c> (0 for a new list).</summary>
public sealed class CustomerPriceListSpec : Specification<PriceList>
{
    public CustomerPriceListSpec(int customerId, int exceptId) => Query.Where(l => l.CustomerId == customerId && l.Id != exceptId).AsNoTracking();
}
