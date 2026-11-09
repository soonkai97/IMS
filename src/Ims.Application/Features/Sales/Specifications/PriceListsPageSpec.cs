using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>One page of price lists: the default list first, then by name.</summary>
public sealed class PriceListsPageSpec : Specification<PriceList, PriceListListItem>
{
    public PriceListsPageSpec(PriceListFilter f)
    {
        PriceListFilters.Apply(Query, f);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).ThenBy(l => l.Id).Skip((page - 1) * size).Take(size)
            .Select(l => new PriceListListItem(l.PublicId, l.Name, l.IsDefault, l.Customer == null ? null : l.Customer.PublicId, l.Customer == null ? null : l.Customer.Name, l.Items.Count(), l.RowVersion));
    }
}
