using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary>
/// The stock list restricted to low stock (<c>lowOnly=true</c>): exactly the set the low-stock list, its export, the Home count and the menu badge show
/// (<see cref="LowStockRule"/>), shaped like stock rows. That includes the active products with a safety level that have never been stocked: they have no
/// balance row, so a table of balances cannot list them, and each appears once per active warehouse in scope with a quantity of zero and no cost. The other
/// filters of <see cref="StockFilter"/> (warehouse, product, category, search) and its sort apply to both kinds of row.
/// </summary>
public interface ILowOnlyStockReader
{
    Task<int> CountAsync(StockFilter filter, IReadOnlyCollection<int>? scope, ProductSearch? search, CancellationToken ct);

    /// <param name="paged">False returns every row up to the export cap.</param>
    Task<IReadOnlyList<StockRow>> ListAsync(StockFilter filter, IReadOnlyCollection<int>? scope, ProductSearch? search, bool paged, CancellationToken ct);
}
