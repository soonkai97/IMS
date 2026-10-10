namespace Ims.Application.Features.Stock;

public record StockRow(int ProductId, string Sku, string ProductName, string? CategoryName, string UomCode, int WarehouseId,
    string WarehouseCode, string WarehouseName, decimal QtyOnHand, decimal QtyInTransit, decimal AvgCost, decimal Value, decimal SafetyStock, bool IsLow);
