using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Balance after a ledger row = the balance now minus the sum of the movements booked after that row (same product and warehouse). Three queries:
/// the balances, the sum of everything newer than the page per warehouse, and the rows inside the span of the page (so rows that a date filter hid
/// between two rows of the page are still counted). All parameters, no raw SQL, and no per-row query.
/// </summary>
public sealed class EfLedgerBalanceReader(AppDbContext db) : ILedgerBalanceReader
{
    public async Task<IReadOnlyDictionary<long, decimal>> BalanceAfterAsync(int productId, IReadOnlyCollection<int>? warehouseScope, int? warehouseId,
        IReadOnlyCollection<long> pageRowIds, CancellationToken ct)
    {
        if (pageRowIds.Count == 0) return new Dictionary<long, decimal>();
        var top = pageRowIds.Max();
        var bottom = pageRowIds.Min();

        var entries = db.Set<StockLedgerEntry>().Where(l => l.ProductId == productId);
        var balances = db.Set<StockBalance>().Where(b => b.ProductId == productId);
        if (warehouseScope is not null)
        {
            entries = entries.Where(l => warehouseScope.Contains(l.WarehouseId));
            balances = balances.Where(b => warehouseScope.Contains(b.WarehouseId));
        }
        if (warehouseId is { } only)
        {
            entries = entries.Where(l => l.WarehouseId == only);
            balances = balances.Where(b => b.WarehouseId == only);
        }

        var onHand = await balances.Select(b => new { b.WarehouseId, b.QtyOnHand }).ToDictionaryAsync(b => b.WarehouseId, b => b.QtyOnHand, ct);
        // Booked after the newest row of the page: nothing of it is on the page, so it is one sum per warehouse.
        var newer = await entries.Where(l => l.Id > top).GroupBy(l => l.WarehouseId).Select(g => new { WarehouseId = g.Key, Sum = g.Sum(l => l.QtyDelta) })
            .ToDictionaryAsync(g => g.WarehouseId, g => g.Sum, ct);
        // The rows from the oldest to the newest of the page, newest first: walk down and subtract each row's movement after reading its balance.
        var span = await entries.Where(l => l.Id >= bottom && l.Id <= top).OrderByDescending(l => l.Id)
            .Select(l => new { l.Id, l.WarehouseId, l.QtyDelta }).ToListAsync(ct);

        var wanted = pageRowIds.ToHashSet();
        var after = new Dictionary<long, decimal>();
        var bookedLater = new Dictionary<int, decimal>();
        foreach (var row in span)
        {
            if (!onHand.TryGetValue(row.WarehouseId, out var balance)) continue;
            var later = bookedLater.GetValueOrDefault(row.WarehouseId, newer.GetValueOrDefault(row.WarehouseId));
            if (wanted.Contains(row.Id)) after[row.Id] = balance - later;
            bookedLater[row.WarehouseId] = later + row.QtyDelta;
        }
        return after;
    }
}
