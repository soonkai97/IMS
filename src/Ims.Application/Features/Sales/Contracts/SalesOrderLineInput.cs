namespace Ims.Application.Features.Sales;

/// <param name="Qty">In the unit named by <c>uomId</c>; at most 4 decimal places.</param>
/// <param name="UomId">Any unit the product converts to its base unit; null means the base unit.</param>
/// <param name="UnitPrice">The price per ENTERED unit, in money (at most 2 decimal places, never negative). 0.00 is accepted on a draft; submitting it is refused (PL3.3).</param>
public record SalesOrderLineInput(Guid ProductId, decimal Qty, Guid? UomId, decimal UnitPrice, string? Remark);
