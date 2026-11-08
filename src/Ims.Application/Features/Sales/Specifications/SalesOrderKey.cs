namespace Ims.Application.Features.Sales;

/// <summary>The integer key of a sales order and the integer key of its warehouse.</summary>
public sealed record SalesOrderKey(int Id, int WarehouseId);
