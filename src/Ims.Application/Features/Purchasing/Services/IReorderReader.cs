namespace Ims.Application.Features.Purchasing;

/// <summary>
/// Reorder suggestions for ONE warehouse, computed live in the database (implemented in Infrastructure: it joins the stock balances to a grouped sum of the open order lines). The low-stock
/// definition is the shared one; the rows are the low ones whose suggested quantity (<see cref="Ims.Domain.ReorderRule"/>) is above zero. The count and the page come from the same query.
/// </summary>
public interface IReorderReader
{
    /// <param name="warehouseScope">Null = all warehouses; it is also the set the "never stocked" test looks at.</param>
    Task<int> CountAsync(IReadOnlyCollection<int>? warehouseScope, int warehouseId, string? search, CancellationToken ct);

    /// <summary>One page ordered by SKU (a total order, so a walk through the pages never repeats a row).</summary>
    Task<IReadOnlyList<ReorderSuggestion>> PageAsync(IReadOnlyCollection<int>? warehouseScope, int warehouseId, string? search, int page, int pageSize, CancellationToken ct);
}
