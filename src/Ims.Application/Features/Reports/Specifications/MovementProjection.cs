using System.Linq.Expressions;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

internal static class MovementProjection
{
    public static readonly Expression<Func<StockDocumentLine, MovementRow>> Expression = l => new MovementRow(
        l.Document.PostedAt!.Value, l.Document.No, l.Document.Type, l.Document.Status, l.Document.Warehouse.Code,
        l.Document.Partner != null ? l.Document.Partner.Name : null, l.Product.Sku, l.Product.Name, l.Qty, l.Uom.Code, l.BaseQty,
        l.UnitCost, l.BaseQty * l.UnitCost);
}
