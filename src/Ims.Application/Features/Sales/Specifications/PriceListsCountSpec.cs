using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class PriceListsCountSpec : Specification<PriceList>
{
    public PriceListsCountSpec(PriceListFilter f) => PriceListFilters.Apply(Query, f);
}
