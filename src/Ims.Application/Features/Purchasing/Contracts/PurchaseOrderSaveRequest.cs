namespace Ims.Application.Features.Purchasing;

/// <summary>A whole purchase-order draft: every field is sent on every save (there is no "absent = unchanged"), and the lines replace the old ones.</summary>
/// <param name="WarehouseId">The ship-to warehouse. It cannot change after the order is created: it carries the number.</param>
/// <param name="SupplierId">An active partner of type Supplier.</param>
public record PurchaseOrderSaveRequest(Guid WarehouseId, Guid SupplierId, string? Remark, IReadOnlyList<PurchaseOrderLineInput> Lines);
