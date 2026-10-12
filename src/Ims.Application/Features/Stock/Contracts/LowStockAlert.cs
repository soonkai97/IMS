namespace Ims.Application.Features.Stock;

/// <param name="QtyOnHand">Zero for a product that is stocked in no warehouse the caller can see (it has no balance row there: it is listed once, with no warehouse). Negative when stock went below zero.</param>
/// <param name="WarehouseId">Null (with the code and the name) only for a product stocked in no warehouse the caller can see. It appears ONCE, not once per warehouse.</param>
/// <param name="Shortfall">Safety stock minus on hand: how many units are missing (more than the safety level when the balance is negative).</param>
/// <param name="IsNegative">On hand is below zero: show it as a warning, not just as "low".</param>
public record LowStockAlert(Guid ProductId, string Sku, string ProductName, Guid? WarehouseId, string? WarehouseCode, string? WarehouseName,
    decimal QtyOnHand, decimal SafetyStock, decimal Shortfall, bool IsNegative);
