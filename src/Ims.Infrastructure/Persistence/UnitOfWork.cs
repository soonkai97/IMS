using Ims.Application;
using Ims.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Commits staged changes and runs transactional work with retry.
/// Deadlock policy (no deadlock may reach the user):
///  1. Prevent: code touches rows in one global order (document sequence, document, balances by ProductId, ledger) and keeps
///     transactions short with no I/O inside; the database runs READ_COMMITTED_SNAPSHOT so readers never block writers;
///     concurrency control is optimistic (rowversion), so no long-held row locks.
///  2. Recover: if SQL Server still picks this session as a deadlock victim (error 1205) or a lock times out (1222), the whole
///     transaction is rolled back and re-run, after a short randomised back-off.
/// </summary>
public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyException(Messages.ConcurrencyConflict, e);
        }
        catch (DbUpdateException e) when (IsRetriable(e))
        {
            // A unique key raced (document number, balance row), or this transaction was the deadlock victim.
            throw new ConcurrencyException(Messages.ConcurrencyConflict, e);
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        // Already inside a transaction (nested use): join it, the outer scope owns commit and retry.
        if (db.Database.CurrentTransaction is not null) return await work();

        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await work();
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception e) when (IsRetriable(e))
            {
                await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear(); // reload fresh state on the next attempt

                if (attempt >= Limits.MaxRetryAttempts)
                    throw e as ConcurrencyException ?? new ConcurrencyException(Messages.ConcurrencyConflict, e);

                await Task.Delay(Backoff(attempt), ct);
            }
        }
    }

    /// <summary>Randomised linear back-off: spreads colliding retries apart.</summary>
    internal static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(attempt * (Limits.RetryBaseDelayMs + Random.Shared.Next(Limits.RetryJitterMs)));

    internal static bool IsRetriable(Exception e) => e switch
    {
        ConcurrencyException => true,
        DbUpdateConcurrencyException => true,
        SqlException s => IsDeadlockOrTimeout(s) || IsUniqueViolation(s),
        DbUpdateException { InnerException: SqlException s } => IsDeadlockOrTimeout(s) || IsUniqueViolation(s),
        _ => false
    };

    private static bool IsDeadlockOrTimeout(SqlException e) => e.Number is SqlErrors.Deadlock or SqlErrors.LockTimeout;
    private static bool IsUniqueViolation(SqlException e) => e.Number is SqlErrors.UniqueIndex or SqlErrors.UniqueConstraint;
}
