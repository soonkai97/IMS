namespace Ims.Application;

/// <summary>
/// Totals over the stock balances computed by the database (SUM / COUNT), so the result is one small row however many
/// balances exist. Implemented in Infrastructure because aggregation is not expressible through specifications.
/// </summary>
public interface IStockSummaryReader
{
    /// <param name="warehouseScope">Null = all warehouses.</param>
    Task<(decimal TotalValue, int SkuCount, int LowCount)> GetAsync(IReadOnlyCollection<int>? warehouseScope, CancellationToken ct);
}
