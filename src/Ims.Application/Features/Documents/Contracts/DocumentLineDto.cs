namespace Ims.Application.Features.Documents;

public record DocumentLineDto(int Id, int ProductId, string Sku, string ProductName, decimal Qty, int UomId, string UomCode,
    decimal BaseQty, decimal UnitCost, string? Remark);
