namespace Ims.Application;

/// <summary>
/// Makes concurrent writers of the same hot data wait for each other instead of racing, failing their version check and retrying.
/// Locks are owned by the surrounding transaction and released when it ends. Resources are always locked in a fixed order, so two
/// transactions can never wait on each other in a cycle (no deadlock). Different resources never block each other.
/// </summary>
public interface ITransactionLocker
{
    /// <summary>Serialises postings that change the same products in one warehouse.</summary>
    /// <exception cref="ConcurrencyException">The lock could not be obtained in time; the whole transaction is retried.</exception>
    Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct);

    /// <summary>Serialises allocation of the next document number for one document type, warehouse and month.</summary>
    Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct);
}
