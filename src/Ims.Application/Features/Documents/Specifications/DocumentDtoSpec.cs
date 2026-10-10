using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentDtoSpec : Specification<StockDocument, DocumentDto>
{
    public DocumentDtoSpec(int id) =>
        Query.AsNoTracking().Where(d => d.Id == id).Select(d => new DocumentDto(
            d.Id, d.Type, d.No, d.Status, d.WarehouseId, d.Warehouse.Name, d.PartnerId, d.Partner != null ? d.Partner.Name : null,
            d.Purpose, d.Remark, d.RequiresApproval, d.RejectionReason, d.CancelReason, d.ReversalReason, d.NegativeStockOverrideReason,
            d.CreatedByUser.Username, d.CreatedAt, d.SubmittedBy, d.SubmittedByUser != null ? d.SubmittedByUser.Username : null, d.SubmittedAt,
            d.ApprovedByUser != null ? d.ApprovedByUser.Username : null, d.ApprovedAt,
            d.PostedByUser != null ? d.PostedByUser.Username : null, d.PostedAt,
            d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.BaseQty * l.UnitCost),
            d.Lines.OrderBy(l => l.Id).Select(l => new DocumentLineDto(l.Id, l.ProductId, l.Product.Sku, l.Product.Name, l.Qty, l.UomId,
                l.Uom.Code, l.BaseQty, l.UnitCost, l.Remark)).ToList(),
            new List<string>()));
}
