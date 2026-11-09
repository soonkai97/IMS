using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The items of one list for some products. Reads the unique index of (list, product, unit), which includes the price.</summary>
public sealed class PriceListItemPricesSpec : Specification<PriceListItem, PriceListItemPrice>
{
    public PriceListItemPricesSpec(int priceListId, IReadOnlyCollection<int> productIds) =>
        Query.Where(i => i.PriceListId == priceListId && productIds.Contains(i.ProductId)).AsNoTracking().Select(i => new PriceListItemPrice(i.ProductId, i.UomId, i.ListPrice));
}
