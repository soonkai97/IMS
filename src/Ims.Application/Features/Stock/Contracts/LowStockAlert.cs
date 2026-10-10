namespace Ims.Application.Features.Stock;

public record LowStockAlert(int ProductId, string Sku, string ProductName, int WarehouseId, string WarehouseCode,
    decimal QtyOnHand, decimal SafetyStock, decimal Shortfall);
