using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>
/// A <see cref="MovementRow"/> together with the keys that put it in the report's order (document id and line id break ties). The query returns
/// these so the posting rows and the reversal rows can be merged by the database; the keys never reach the client. Plain properties, not a
/// constructor, so both kinds of row have the same shape and EF can concatenate them.
/// </summary>
public sealed class MovementEntry
{
    public DateTime At { get; init; }
    public int DocumentId { get; init; }
    public int LineId { get; init; }
    public string DocNo { get; init; } = "";
    public DocumentType Type { get; init; }
    public DocumentStatus Status { get; init; }
    public string WarehouseCode { get; init; } = "";
    public string? Partner { get; init; }
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal Qty { get; init; }
    public string UomCode { get; init; } = "";
    public decimal BaseQty { get; init; }
    public decimal StockEffect { get; init; }
    public decimal UnitCost { get; init; }
    public decimal Amount { get; init; }
    public bool IsReversal { get; init; }

    public MovementRow ToRow() => new(At, DocNo, Type, Status, WarehouseCode, Partner, Sku, ProductName, Qty, UomCode, BaseQty, StockEffect,
        UnitCost, Amount, IsReversal);
}
