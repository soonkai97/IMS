using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentDtoSpec : Specification<StockDocument, DocumentDto>
{
    public DocumentDtoSpec(int id) =>
        Query.AsNoTracking().Where(d => d.Id == id).Select(d => new DocumentDto(
            d.Id, d.Type, d.No, d.Status, d.WarehouseId, d.Warehouse.Name, d.PartnerId, d.Partner != null ? d.Partner.Name : null,
            d.Purpose, d.Remark, d.RequiresApproval, d.RequiresOverride, d.IsLarge,
            d.Status == DocumentStatus.Draft && d.RejectionReason != null,
            d.RejectionReason, d.CancelReason, d.ReversalReason, d.NegativeStockOverrideReason,
            d.CreatedBy, d.CreatedByUser.Username, d.CreatedByUser.DisplayName, d.CreatedAt,
            d.SubmittedBy, d.SubmittedByUser != null ? d.SubmittedByUser.Username : null,
            d.SubmittedByUser != null ? d.SubmittedByUser.DisplayName : null, d.SubmittedAt,
            d.ApprovedByUser != null ? d.ApprovedByUser.Username : null, d.ApprovedByUser != null ? d.ApprovedByUser.DisplayName : null, d.ApprovedAt,
            d.PostedByUser != null ? d.PostedByUser.Username : null, d.PostedByUser != null ? d.PostedByUser.DisplayName : null, d.PostedAt,
            d.RejectedBy, d.RejectedByUser != null ? d.RejectedByUser.Username : null,
            d.RejectedByUser != null ? d.RejectedByUser.DisplayName : null, d.RejectedAt,
            d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.Amount),
            d.Lines.OrderBy(l => l.Id).Select(l => new DocumentLineDto(l.Id, l.ProductId, l.Product.Sku, l.Product.Name, l.Qty, l.UomId,
                l.Uom.Code, l.BaseQty, l.CostFromAverage ? null : l.EnteredUnitCost, Math.Round(l.UnitCost, Money.UnitPriceDecimals), l.Amount, l.CostFromAverage, l.Remark, null)).ToList(),
            new List<string>(), new List<ApprovalTriggerDto>(), d.RowVersion));
}
