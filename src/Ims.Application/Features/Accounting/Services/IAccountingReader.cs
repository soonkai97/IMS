namespace Ims.Application.Features.Accounting;

/// <summary>
/// The lines of the accounting file: the posting row of every line of a posted or reversed document dated in the period, plus a reversal row for every line of a document reversed in
/// the period, oldest first. Implemented in Infrastructure because the two kinds of row come from different predicates that one specification cannot merge (the same reason as
/// <c>IMovementReader</c>, whose date rules and filters this reader shares, so the file and the movements report always agree on what falls in a period).
/// </summary>
public interface IAccountingReader
{
    /// <summary>
    /// At most <paramref name="limit"/> rows in file order. The caller asks for one more than it can write and refuses when it gets it, so a file is never cut short. The warehouse is the integer id
    /// the handler translated from the public id; <paramref name="warehouseScope"/> null = every warehouse.
    /// </summary>
    Task<IReadOnlyList<AccountingEntry>> ListAsync(DateOnly from, DateOnly to, int? warehouseId, IReadOnlyCollection<int>? warehouseScope, int limit, CancellationToken ct);
}
