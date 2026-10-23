namespace Ims.Application.Features.Sales;

/// <summary>A whole sales-order draft: every field is sent on every save (there is no "absent = unchanged"), and the lines replace the old ones. The same product may appear on several lines (different units, prices or remarks): anything that refers to a line, such as delivering against the order (PL3.5), must use its line number, never the product.</summary>
/// <param name="WarehouseId">The ship-from warehouse. It cannot change after the order is created: it carries the number.</param>
/// <param name="CustomerId">An active partner of type Customer.</param>
public record SalesOrderSaveRequest(Guid WarehouseId, Guid CustomerId, string? Remark, IReadOnlyList<SalesOrderLineInput> Lines);
