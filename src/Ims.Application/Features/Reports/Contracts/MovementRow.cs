using Ims.Domain;
namespace Ims.Application.Features.Reports;

public record MovementRow(DateTime PostedAt, string DocNo, DocumentType Type, DocumentStatus Status, string WarehouseCode, string? Partner,
    string Sku, string ProductName, decimal Qty, string UomCode, decimal BaseQty, decimal UnitCost, decimal Amount);
