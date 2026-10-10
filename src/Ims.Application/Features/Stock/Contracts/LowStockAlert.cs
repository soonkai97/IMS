namespace Ims.Application.Features.Stock;

public record LowStockAlert(int ProductId, string Sku, string ProductName, int WarehouseId, string WarehouseCode, string WarehouseName,
    decimal QtyOnHand, decimal SafetyStock, decimal Shortfall);
