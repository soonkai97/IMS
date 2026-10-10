using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Reads the on-hand quantities; the rule itself lives in <see cref="StockAvailabilityPolicy"/>.</summary>
public class StockAvailability(IReadRepository<StockBalance> balances) : IStockAvailability, IAppService
{
    public async Task EnsureAvailableAsync(StockDocument document, CancellationToken ct)
    {
        var productIds = document.Lines.Select(l => l.ProductId).Distinct().ToList();
        var onHand = (await balances.ListAsync(new BalancesSpec(document.WarehouseId, productIds, tracked: false), ct))
            .ToDictionary(b => b.ProductId, b => b.QtyOnHand);
        StockAvailabilityPolicy.EnsureAvailable(document, onHand);
    }
}
