using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Reads the on-hand quantities; the rule itself lives in <see cref="StockAvailabilityPolicy"/>.</summary>
public class StockAvailability(IReadRepository<StockBalance> balances) : IStockAvailability, IAppService
{
    public async Task EnsureAvailableAsync(StockDocument document, CancellationToken ct) =>
        StockAvailabilityPolicy.EnsureAvailable(document, await OnHandOfAsync(document, ct));

    public async Task<bool> IsShortAsync(StockDocument document, CancellationToken ct) =>
        StockAvailabilityPolicy.IsShort(document, await OnHandOfAsync(document, ct));

    public async Task<IReadOnlyDictionary<int, decimal>> OnHandAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct) =>
        (await balances.ListAsync(new BalancesSpec(warehouseId, productIds, tracked: false), ct)).ToDictionary(b => b.ProductId, b => b.QtyOnHand);

    private Task<IReadOnlyDictionary<int, decimal>> OnHandOfAsync(StockDocument document, CancellationToken ct) =>
        OnHandAsync(document.WarehouseId, document.Lines.Select(l => l.ProductId).Distinct().ToList(), ct);
}
