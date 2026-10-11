using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="AvgCost">Average cost per base unit. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="Value">Quantity on hand times average cost. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="IsLow">Low by the one definition (<see cref="LowStockRule"/>): the product is active and its quantity is below zero, or below its safety stock (when it has one; a quantity equal to the safety stock is not low). The low-stock list, its count, the Home tile and the badge use the same rule.</param>
/// <param name="IsNegative">On hand is below zero: show a warning, not just a "Low" chip (also true when the product has no safety stock).</param>
public record StockRow(int ProductId, string Sku, string ProductName, string? CategoryName, string UomCode, int WarehouseId,
    string WarehouseCode, string WarehouseName, decimal QtyOnHand, decimal QtyInTransit, [property: VisibleWith(Permissions.ValuationView)] decimal? AvgCost,
    [property: VisibleWith(Permissions.ValuationView)] decimal? Value, decimal SafetyStock, bool IsLow,
    bool IsNegative);
