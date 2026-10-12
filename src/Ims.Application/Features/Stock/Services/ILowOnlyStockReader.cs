using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>
/// The stock list restricted to low stock (<c>lowOnly=true</c>): exactly the set the low-stock list, its export, the Home count and the menu badge show
/// (<see cref="LowStockRule"/>), shaped like stock rows. That includes the active products with a safety level that have no stock in any warehouse the caller can see: they
/// have no balance row, so a table of balances cannot list them, and each appears ONCE with no warehouse, a quantity of zero and no average cost or value (null:
/// nothing was ever bought, so there is no average, which is not an average of zero). The other filters of
/// <see cref="StockFilter"/> (warehouse, product, category, search) and its sort apply to both kinds of row.
/// </summary>
public interface ILowOnlyStockReader
{
    /// <param name="warehouseId">The filter's warehouse and category as integer ids (the handler translates the public ids; see <c>IdMapExtensions</c>); null = no such filter.</param>
    Task<int> CountAsync(StockFilter filter, int? warehouseId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search, CancellationToken ct);

    /// <param name="paged">False returns every row up to the export cap.</param>
    Task<IReadOnlyList<StockRow>> ListAsync(StockFilter filter, int? warehouseId, int? categoryId, IReadOnlyCollection<int>? scope, ProductSearch? search, bool paged, CancellationToken ct);
}
