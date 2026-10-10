using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <param name="UnitCost">Per base unit. Null for a caller without <c>cost.view</c>.</param>
/// <param name="Amount">Base quantity times unit cost. Null for a caller without <c>cost.view</c>.</param>
public record MovementRow(DateTime PostedAt, string DocNo, DocumentType Type, DocumentStatus Status, string WarehouseCode, string? Partner,
    string Sku, string ProductName, decimal Qty, string UomCode, decimal BaseQty,
    [property: VisibleWith(Permissions.CostView)] decimal? UnitCost, [property: VisibleWith(Permissions.CostView)] decimal? Amount);
