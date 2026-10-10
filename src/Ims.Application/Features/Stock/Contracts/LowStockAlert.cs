namespace Ims.Application.Features.Stock;

/// <param name="QtyOnHand">Zero for a product that has never been stocked (it has no balance row). Negative when stock went below zero.</param>
/// <param name="Shortfall">Safety stock minus on hand: how many units are missing (more than the safety level when the balance is negative).</param>
/// <param name="IsNegative">On hand is below zero: show it as a warning, not just as "low".</param>
public record LowStockAlert(int ProductId, string Sku, string ProductName, int WarehouseId, string WarehouseCode, string WarehouseName,
    decimal QtyOnHand, decimal SafetyStock, decimal Shortfall, bool IsNegative);
