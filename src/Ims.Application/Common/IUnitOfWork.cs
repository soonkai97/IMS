namespace Ims.Application;

public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyException">The data was changed by another request, or a unique key raced.</exception>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction and commits it. On <see cref="ConcurrencyException"/>
    /// the transaction is rolled back, tracked state is discarded and the work is re-run from the start (BR-12).
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}
