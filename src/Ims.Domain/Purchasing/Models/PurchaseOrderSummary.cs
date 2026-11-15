namespace Ims.Domain;

/// <summary>
/// What the audit trail prints about a purchase order when it is created or updated: its status, warehouse, supplier, how many lines it has and the total of its line amounts. A fact, not an entity.
/// The amount is a <c>decimal</c> exactly as the lines summed it, NOT a <see cref="Money"/>: the audit row prints a decimal at the scale it has (12, 12.5 and 12.50 are three different rows, and a total
/// read back from the database has two places), and a <see cref="Money"/> always has two, so it would change the stored text of a row that the audit pins (<c>PurchaseOrderAuditRowsTests</c>) keep byte for byte.
/// </summary>
public sealed record PurchaseOrderSummary(PurchaseOrderStatus Status, int WarehouseId, int SupplierId, int LineCount, decimal Amount);
