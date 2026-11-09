using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class PriceLookup(IReadRepository<PriceList> lists, IReadRepository<PriceListItem> items) : IPriceLookup, IAppService
{
    public async Task<IReadOnlyDictionary<(int ProductId, int UomId), ListedPrice>> FindAsync(int customerId, IReadOnlyCollection<(int ProductId, int UomId)> keys, CancellationToken ct)
    {
        var found = new Dictionary<(int ProductId, int UomId), ListedPrice>();
        if (keys.Count == 0) return found;
        var productIds = keys.Select(k => k.ProductId).Distinct().ToList();

        // The lists are resolved first, live ones only: the items of a deleted list are soft-deleted with it, but the lookup never depends on that.
        var customerList = await lists.FirstOrDefaultAsync(new CustomerPriceListSpec(customerId, 0), ct);
        var defaultList = await lists.FirstOrDefaultAsync(new DefaultPriceListSpec(0), ct);
        foreach (var (list, source) in new[] { (customerList, PriceSource.Customer), (defaultList, PriceSource.Default) })
        {
            if (list is null) continue;
            foreach (var row in await items.ListAsync(new PriceListItemPricesSpec(list.Id, productIds), ct))
                if (keys.Contains((row.ProductId, row.UomId))) found.TryAdd((row.ProductId, row.UomId), new ListedPrice(row.ListPrice, source));
        }
        return found;
    }
}
