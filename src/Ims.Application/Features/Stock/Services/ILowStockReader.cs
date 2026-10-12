namespace Ims.Application.Features.Stock;

/// <summary>
/// The low-stock definition in one place, implemented in Infrastructure because it joins two sources (stock balances, and products that have no
/// balance yet) which a specification over one table cannot express. The list, its total, the Home count and the export all come from here, so they
/// always agree.
/// <para>
/// A product is low in a warehouse when it is active and either its on-hand quantity is below zero, or it has a safety level above zero and the
/// on-hand quantity is at or below it (design 3.8: "on-hand &lt;= safety stock"). An active product with a safety level that has no balance row in ANY active
/// warehouse the caller can see is never stocked: it is listed once, with no warehouse and an on-hand quantity of zero (it has no balance row, so a table of balances
/// cannot show it). Only the caller's warehouses count, so a scoped caller learns nothing about stock elsewhere. A warehouse filter leaves those products out.
/// </para>
/// </summary>
public interface ILowStockReader
{
    /// <param name="warehouseScope">Null = all warehouses.</param>
    /// <param name="warehouseId">Optional single warehouse (still inside the scope).</param>
    Task<int> CountAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, CancellationToken ct);

    /// <summary>One page, worst first (largest shortfall as a share of the safety level, then in units), then SKU and warehouse code.</summary>
    Task<IReadOnlyList<LowStockAlert>> PageAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, int page, int pageSize, CancellationToken ct);

    /// <summary>The same order without paging, capped at <paramref name="limit"/> rows (exports).</summary>
    Task<IReadOnlyList<LowStockAlert>> ListAsync(IReadOnlyCollection<int>? warehouseScope, int? warehouseId, int limit, CancellationToken ct);
}
