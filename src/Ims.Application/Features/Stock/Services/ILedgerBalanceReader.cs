namespace Ims.Application.Features.Stock;

/// <summary>
/// The balance a product held in a warehouse right after each of the given ledger rows (PS-14). It is derived, not stored: the current balance minus
/// everything that was booked after the row, so the ledger stays append-only and nothing can drift. Implemented in Infrastructure because it
/// aggregates a table and joins the balance, which a specification over one table cannot express. A fixed number of queries whatever the page size.
/// </summary>
public interface ILedgerBalanceReader
{
    /// <param name="productId">The one product whose ledger page is being read.</param>
    /// <param name="warehouseScope">Null = all warehouses.</param>
    /// <param name="warehouseId">Optional single warehouse (still inside the scope).</param>
    /// <param name="pageRowIds">Ids of the ledger rows on the page (all of this product).</param>
    /// <returns>Balance after each row, keyed by ledger id; a row it cannot place is absent.</returns>
    Task<IReadOnlyDictionary<long, decimal>> BalanceAfterAsync(int productId, IReadOnlyCollection<int>? warehouseScope, int? warehouseId,
        IReadOnlyCollection<long> pageRowIds, CancellationToken ct);
}
