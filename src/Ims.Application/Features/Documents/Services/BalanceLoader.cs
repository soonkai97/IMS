using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class BalanceLoader(IRepository<StockBalance> balances, ITransactionLocker locker) : IBalanceLoader, IAppService
{
    public async Task<Dictionary<int, StockBalance>> LoadForUpdateAsync(StockDocument document, CancellationToken ct)
    {
        // Ascending ProductId: the global lock order for balance rows (see BalancesSpec).
        var ids = document.Lines.Select(l => l.ProductId).Distinct().Order().ToList();
        // Wait for any posting that is changing the same products, then read their latest committed balances.
        await locker.LockStockAsync(document.WarehouseId, ids, ct);
        var map = (await balances.ListAsync(new BalancesSpec(document.WarehouseId, ids, tracked: true), ct)).ToDictionary(b => b.ProductId);
        foreach (var pid in ids.Where(p => !map.ContainsKey(p)))
        {
            var balance = StockBalance.OpenFor(pid, document.WarehouseId);
            balances.Add(balance);
            map[pid] = balance;
        }
        return map;
    }
}
