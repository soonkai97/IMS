using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="TotalValue">Net value of the stock in the caller's warehouses (quantity x average cost). A negative balance lowers it, so it can be less than the sum of the positive lines. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="SkuCount">Products with stock: distinct products that have a positive quantity in at least one of the caller's warehouses (zero and negative balances are not counted).</param>
/// <param name="LowCount">Product x warehouse pairs on the low-stock list (see <c>GET /api/stock/low-stock</c>): below safety stock, never stocked with a safety level, or negative. Equals that list's total.</param>
public record StockSummary([property: VisibleWith(Permissions.ValuationView)] decimal? TotalValue, int SkuCount, int LowCount);
