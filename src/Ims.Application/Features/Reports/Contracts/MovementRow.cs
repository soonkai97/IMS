using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>One line of the receipt/issue detail. A document reversed since posting has two rows per line: the posting and the reversal.</summary>
/// <param name="PostedAt">When the movement happened: the posting time, or for a reversal row the time of the reversal.</param>
/// <param name="Qty">As entered, unsigned, in the line's own unit. Unchanged by <see cref="StockEffect"/>.</param>
/// <param name="BaseQty">Unsigned, in the product's base unit.</param>
/// <param name="StockEffect">Signed, in base units: positive when the movement adds stock, negative when it removes stock. A receipt adds, an issue removes, and the reversal row of a document does the opposite of its posting. Summed over a product and a period (a date range on <c>PostedAt</c>) it equals the change in the product's stock balance.</param>
/// <param name="IsReversal">The row is the reversal of the posting shown in the row for the same document and line.</param>
public record MovementRow(DateTime PostedAt, string DocNo, DocumentType Type, DocumentStatus Status, string WarehouseCode, string? Partner,
    string Sku, string ProductName, decimal Qty, string UomCode, decimal BaseQty, decimal StockEffect, decimal UnitCost, decimal Amount,
    bool IsReversal);
