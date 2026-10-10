namespace Ims.Application.Features.Stock;

/// <param name="IsLow">Below the product's safety stock (when it has one) or below zero.</param>
/// <param name="IsNegative">On hand is below zero: show a warning, not just a "Low" chip (also true when the product has no safety stock).</param>
public record StockRow(int ProductId, string Sku, string ProductName, string? CategoryName, string UomCode, int WarehouseId,
    string WarehouseCode, string WarehouseName, decimal QtyOnHand, decimal QtyInTransit, decimal AvgCost, decimal Value, decimal SafetyStock, bool IsLow,
    bool IsNegative);
