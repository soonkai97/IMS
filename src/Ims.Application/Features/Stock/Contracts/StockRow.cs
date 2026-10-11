using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="AvgCost">Average cost per base unit. Null for a caller without <c>valuation.view</c>, and for a product never stocked in the warehouse (a low-stock row with nothing on hand: there is no average, which is not an average of zero).</param>
/// <param name="Value">Quantity on hand times average cost. Null for a caller without <c>valuation.view</c>, and for a product never stocked in the warehouse.</param>
/// <param name="IsLow">Low by the one definition (<see cref="LowStockRule"/>): the product is active and its quantity is below zero, or at or below its safety stock (when it has one; design 3.8, "on-hand &lt;= safety stock"). The low-stock list, its count, the Home tile and the badge use the same rule.</param>
/// <param name="IsNegative">On hand is below zero: show a warning, not just a "Low" chip (also true when the product has no safety stock).</param>
public record StockRow(int ProductId, string Sku, string ProductName, string? CategoryName, string UomCode, int WarehouseId,
    string WarehouseCode, string WarehouseName, decimal QtyOnHand, decimal QtyInTransit, [property: VisibleWith(Permissions.ValuationView)] decimal? AvgCost,
    [property: VisibleWith(Permissions.ValuationView)] decimal? Value, decimal SafetyStock, bool IsLow,
    bool IsNegative);
