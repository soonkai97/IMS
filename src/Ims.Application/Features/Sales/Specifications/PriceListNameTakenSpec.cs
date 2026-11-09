using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>A live list other than <c>exceptId</c> (0 for a new list) with this name (the database compares without regard to case).</summary>
public sealed class PriceListNameTakenSpec : Specification<PriceList>
{
    public PriceListNameTakenSpec(string name, int exceptId) => Query.Where(l => l.Name == name && l.Id != exceptId).AsNoTracking();
}
