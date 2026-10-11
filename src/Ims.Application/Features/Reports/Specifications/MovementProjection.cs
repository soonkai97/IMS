using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>
/// The two shapes of a report row, built from a document line. The sign of <c>StockEffect</c> repeats <see cref="StockDirection"/> as an
/// expression (a receipt adds, an issue removes; a reversal does the opposite); <c>SignedStockEffectTests</c> keeps the two in step.
/// </summary>
internal static class MovementProjection
{
    /// <summary>The posting of the document, dated by its PostedAt.</summary>
    public static readonly Expression<Func<StockDocumentLine, MovementEntry>> Posting = l => new MovementEntry
    {
        At = l.Document.PostedAt!.Value, DocumentId = l.DocumentId, LineId = l.Id, DocNo = l.Document.No, Type = l.Document.Type,
        Status = l.Document.Status, WarehouseCode = l.Document.Warehouse.Code,
        Partner = l.Document.Partner != null ? l.Document.Partner.Name : null, Sku = l.Product.Sku, ProductName = l.Product.Name, Qty = l.Qty,
        UomCode = l.Uom.Code, BaseQty = l.BaseQty, StockEffect = l.Document.Type == DocumentType.Receipt ? l.BaseQty : -l.BaseQty,
        UnitCost = Math.Round(l.UnitCost, Money.UnitPriceDecimals), Amount = l.Amount, IsReversal = false
    };

    /// <summary>The reversal of a Reversed document, dated by its ReversedAt.</summary>
    public static readonly Expression<Func<StockDocumentLine, MovementEntry>> Reversal = l => new MovementEntry
    {
        At = l.Document.ReversedAt!.Value, DocumentId = l.DocumentId, LineId = l.Id, DocNo = l.Document.No, Type = l.Document.Type,
        Status = l.Document.Status, WarehouseCode = l.Document.Warehouse.Code,
        Partner = l.Document.Partner != null ? l.Document.Partner.Name : null, Sku = l.Product.Sku, ProductName = l.Product.Name, Qty = l.Qty,
        UomCode = l.Uom.Code, BaseQty = l.BaseQty, StockEffect = l.Document.Type == DocumentType.Receipt ? -l.BaseQty : l.BaseQty,
        UnitCost = Math.Round(l.UnitCost, Money.UnitPriceDecimals), Amount = l.Amount, IsReversal = true
    };
}
