using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>
/// The two shapes of a row of the accounting file, built from a document line: the posting (dated by the document's PostedAt) and the reversal of a Reversed document (dated by its
/// ReversedAt, so a period adds up to its own change, as in the movements report). Quantities and the unit cost are per BASE unit, the unit of the product, so quantity x unit cost
/// is the amount up to rounding. The sign is applied later, from <see cref="StockDirection"/>, because a query cannot call it.
/// </summary>
internal static class AccountingProjection
{
    // The joins to warehouse, partner, product and unit carry the global filter REC_DELETED = 0 (EF warns about it on a required navigation; the warning is suppressed in AddInfrastructure). A soft-deleted
    // warehouse, product or unit would silently drop its rows from the file. Nothing deletes them today (master data is deactivated, never deleted); whoever adds such a delete must revisit this.

    public static readonly Expression<Func<StockDocumentLine, AccountingEntry>> Posting = l => new AccountingEntry
    {
        At = l.Document.PostedAt!.Value, DocumentId = l.DocumentId, LineId = l.Id, DocNo = l.Document.No, Type = l.Document.Type, Purpose = l.Document.Purpose,
        WarehouseCode = l.Document.Warehouse.Code, Partner = l.Document.Partner != null ? l.Document.Partner.Name : null,
        PartnerTin = l.Document.Partner != null ? l.Document.Partner.Tin : null, PartnerSstRegNo = l.Document.Partner != null ? l.Document.Partner.SstRegNo : null,
        Sku = l.Product.Sku, ProductName = l.Product.Name, HsCode = l.Product.HsCode, SstCode = l.Product.SstCode, BaseUnit = l.Product.BaseUom.Code,
        BaseQty = l.BaseQty, UnitCost = Math.Round(l.UnitCost, Money.UnitPriceDecimals), Amount = l.Amount, IsReversal = false
    };

    public static readonly Expression<Func<StockDocumentLine, AccountingEntry>> Reversal = l => new AccountingEntry
    {
        At = l.Document.ReversedAt!.Value, DocumentId = l.DocumentId, LineId = l.Id, DocNo = l.Document.No, Type = l.Document.Type, Purpose = l.Document.Purpose,
        WarehouseCode = l.Document.Warehouse.Code, Partner = l.Document.Partner != null ? l.Document.Partner.Name : null,
        PartnerTin = l.Document.Partner != null ? l.Document.Partner.Tin : null, PartnerSstRegNo = l.Document.Partner != null ? l.Document.Partner.SstRegNo : null,
        Sku = l.Product.Sku, ProductName = l.Product.Name, HsCode = l.Product.HsCode, SstCode = l.Product.SstCode, BaseUnit = l.Product.BaseUom.Code,
        BaseQty = l.BaseQty, UnitCost = Math.Round(l.UnitCost, Money.UnitPriceDecimals), Amount = l.Amount, IsReversal = true
    };
}
