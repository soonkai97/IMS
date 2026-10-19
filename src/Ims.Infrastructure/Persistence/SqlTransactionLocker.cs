using Ims.Application;
using Ims.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Transaction locks as SQL Server application locks (sp_getapplock, transaction-owned). A hot product (many people issuing the same
/// item) or a busy number sequence is processed one writer at a time, so throughput stays steady and no request starves. The resource
/// name is always a parameter, never concatenated into SQL.
/// </summary>
/// <param name="rolesLockTimeoutMs">How long the roles lock waits; tests shorten it to prove the 409 without a long sleep.</param>
/// <param name="modulesLockTimeoutMs">How long the modules lock waits; tests shorten it likewise.</param>
public sealed class SqlTransactionLocker(AppDbContext db, int rolesLockTimeoutMs = Limits.RolesLockTimeoutMs, int modulesLockTimeoutMs = Limits.ModulesLockTimeoutMs) : ITransactionLocker
{
    /// <summary>sp_getapplock returns 0 or 1 when the lock is granted and a negative number when it is not (timeout, cancelled, deadlock victim, error).</summary>
    private const int NotGranted = 0;

    public async Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        foreach (var productId in productIds.Distinct().Order()) // ascending: the global lock order
            await AcquireAsync($"stock:{warehouseId}:{productId}", Limits.StockLockTimeoutMs, retry: true, ct);
    }

    public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) =>
        AcquireAsync($"docseq:{prefix}:{yearMonth}:{warehouseId}", Limits.StockLockTimeoutMs, retry: true, ct);

    public Task LockRolesAsync(CancellationToken ct) => AcquireAsync("roles", rolesLockTimeoutMs, retry: false, ct);

    public Task LockModulesAsync(CancellationToken ct) => AcquireAsync("modules", modulesLockTimeoutMs, retry: false, ct, Messages.ModulesLockBusy);

    private async Task AcquireAsync(string resource, int timeoutMs, bool retry, CancellationToken ct, string busyMessage = Messages.LockBusy)
    {
        // An application lock owned by the transaction needs one. Outside a transaction there is nothing to hold it for.
        if (db.Database.CurrentTransaction is null) return;

        int result;
        try
        {
            result = (await db.Database.SqlQuery<int>($"""
                DECLARE @r int;
                EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeoutMs};
                SELECT @r AS Value
                """).ToListAsync(ct)).Single();
        }
        catch (SqlException) when (ct.IsCancellationRequested)
        {
            // The request timed out or the caller left while the wait was on: the driver reports it as a SqlException, which would end as a 500.
            throw new OperationCanceledException(ct);
        }
        if (result >= NotGranted) return;
        throw retry ? new ConcurrencyException(Messages.ConcurrencyConflict) : new LockTimeoutException(busyMessage);
    }
}
