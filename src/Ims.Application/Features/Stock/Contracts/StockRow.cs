using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="AvgCost">Average cost per base unit. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="Value">Quantity on hand times average cost. Null for a caller without <c>valuation.view</c>.</param>
/// <param name="IsLow">Below the product's safety stock (when it has one) or below zero.</param>
/// <param name="IsNegative">On hand is below zero: show a warning, not just a "Low" chip (also true when the product has no safety stock).</param>
public record StockRow(int ProductId, string Sku, string ProductName, string? CategoryName, string UomCode, int WarehouseId,
    string WarehouseCode, string WarehouseName, decimal QtyOnHand, decimal QtyInTransit, [property: VisibleWith(Permissions.ValuationView, UnitPrice = true)] decimal? AvgCost,
    [property: VisibleWith(Permissions.ValuationView)] decimal? Value, decimal SafetyStock, bool IsLow,
    bool IsNegative);
