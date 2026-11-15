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
/// Domain events (DDD-11a): every save first lets the in-transaction handlers run (they stage changes that this very save writes). The after-commit handlers run once the outermost transaction has
/// committed: after the save when no transaction is open, otherwise after <see cref="ExecuteAsync{T}"/> has committed; never for an attempt that was retried or a use case that failed.
/// </summary>
public sealed class UnitOfWork(AppDbContext db, DomainEventDispatcher events) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (events.IsRunning) throw new InvalidOperationException(DomainEventDispatcher.HandlerSaved);

        var ambient = db.Database.CurrentTransaction is not null;
        var queuedBefore = events.QueuedCount;
        int saved;
        try
        {
            await events.DispatchAsync(ct); // before the save: the REC_* stamping and the row audit run inside it, so whatever the handlers staged is covered
            saved = await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            events.DiscardFrom(queuedBefore); // what this save queued was never written, even if the body catches the failure and commits
            throw new ConcurrencyException(Messages.ConcurrencyConflict, e);
        }
        catch (DbUpdateException e) when (IsRetriable(e))
        {
            // A unique key raced (document number, balance row), or this transaction was the deadlock victim.
            events.DiscardFrom(queuedBefore);
            throw new ConcurrencyException(Messages.ConcurrencyConflict, e);
        }
        catch
        {
            events.DiscardFrom(queuedBefore);
            throw;
        }

        // Inside a transaction the owner (ExecuteAsync) flushes once it has committed; with none open this save was the commit.
        if (!ambient) await events.FlushAfterCommitAsync();
        return saved;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        // A handler runs inside the dispatcher: a transaction of its own would drop the queued after-commit handlers (the retry loop resets the queue).
        if (events.IsRunning) throw new InvalidOperationException(DomainEventDispatcher.HandlerExecuted);

        // Already inside a transaction (nested use): join it, the outer scope owns commit, retry and the after-commit handlers.
        if (db.Database.CurrentTransaction is not null) return await work();

        T result;
        try
        {
            result = await RunInTransactionAsync(work, ct);
        }
        catch
        {
            events.Discard(); // the use case failed: nothing it raised happened
            throw;
        }

        // The transaction is committed and disposed. Never run this inside the retry loop: a retriable exception after the commit would run committed work again.
        await events.FlushAfterCommitAsync();
        return result;
    }

    private async Task<T> RunInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            events.Discard();
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
                events.Discard();         // and forget what the discarded attempt raised

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
        LockTimeoutException => false,   // the lock holder is slow: answer 409 now instead of waiting the same time again
        ConcurrencyException => true,
        DbUpdateConcurrencyException => true,
        SqlException s => IsDeadlockOrTimeout(s) || IsUniqueViolation(s),
        DbUpdateException { InnerException: SqlException s } => IsDeadlockOrTimeout(s) || IsUniqueViolation(s),
        _ => false
    };

    private static bool IsDeadlockOrTimeout(SqlException e) => e.Number is SqlErrors.Deadlock or SqlErrors.LockTimeout;
    private static bool IsUniqueViolation(SqlException e) => e.Number is SqlErrors.UniqueIndex or SqlErrors.UniqueConstraint;
}
