using Ims.Application;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Transaction locks as SQL Server application locks (sp_getapplock, transaction-owned). A hot product (many people issuing the same
/// item) or a busy number sequence is processed one writer at a time, so throughput stays steady and no request starves. The resource
/// name is always a parameter, never concatenated into SQL.
/// </summary>
public sealed class SqlTransactionLocker(AppDbContext db) : ITransactionLocker
{
    /// <summary>sp_getapplock returns 0 or 1 when the lock is granted and a negative number when it is not (timeout, cancelled, deadlock victim, error).</summary>
    private const int NotGranted = 0;

    public async Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        foreach (var productId in productIds.Distinct().Order()) // ascending: the global lock order
            await AcquireAsync($"stock:{warehouseId}:{productId}", ct);
    }

    public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) =>
        AcquireAsync($"docseq:{prefix}:{yearMonth}:{warehouseId}", ct);

    public Task LockRolesAsync(CancellationToken ct) => AcquireAsync("roles", ct);

    private async Task AcquireAsync(string resource, CancellationToken ct)
    {
        // An application lock owned by the transaction needs one. Outside a transaction there is nothing to hold it for.
        if (db.Database.CurrentTransaction is null) return;

        var result = (await db.Database.SqlQuery<int>($"""
            DECLARE @r int;
            EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {Limits.StockLockTimeoutMs};
            SELECT @r AS Value
            """).ToListAsync(ct)).Single();
        if (result < NotGranted) throw new ConcurrencyException(Messages.ConcurrencyConflict);
    }
}
