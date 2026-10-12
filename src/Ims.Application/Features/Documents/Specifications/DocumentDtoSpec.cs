using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentDtoSpec : Specification<StockDocument, DocumentDto>
{
    public DocumentDtoSpec(int id, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(d => d.Id == id);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId)); // outside the caller's warehouses the document does not exist for them: 404, not 403 (SEC-11)
        Query.Select(d => new DocumentDto(
            d.Id, d.Type, d.No, d.Status, d.Warehouse.PublicId, d.Warehouse.Name, d.Partner != null ? d.Partner.PublicId : (Guid?)null, d.Partner != null ? d.Partner.Name : null,
            d.Purpose, d.Remark, d.RequiresApproval, d.RequiresOverride, d.IsLarge,
            d.Status == DocumentStatus.Draft && d.RejectionReason != null,
            d.RejectionReason, d.CancelReason, d.ReversalReason, d.NegativeStockOverrideReason,
            d.CreatedByUser.PublicId, d.CreatedByUser.Username, d.CreatedByUser.DisplayName, d.CreatedAt,
            d.SubmittedByUser != null ? d.SubmittedByUser.PublicId : (Guid?)null, d.SubmittedByUser != null ? d.SubmittedByUser.Username : null,
            d.SubmittedByUser != null ? d.SubmittedByUser.DisplayName : null, d.SubmittedAt,
            d.ApprovedByUser != null ? d.ApprovedByUser.Username : null, d.ApprovedByUser != null ? d.ApprovedByUser.DisplayName : null, d.ApprovedAt,
            d.PostedByUser != null ? d.PostedByUser.Username : null, d.PostedByUser != null ? d.PostedByUser.DisplayName : null, d.PostedAt,
            d.ReversedByUser != null ? d.ReversedByUser.Username : null, d.ReversedByUser != null ? d.ReversedByUser.DisplayName : null, d.ReversedAt,
            d.RejectedByUser != null ? d.RejectedByUser.PublicId : (Guid?)null, d.RejectedByUser != null ? d.RejectedByUser.Username : null,
            d.RejectedByUser != null ? d.RejectedByUser.DisplayName : null, d.RejectedAt,
            d.Lines.Sum(l => l.BaseQty), d.Lines.Sum(l => l.Amount),
            d.Lines.OrderBy(l => l.Id).Select(l => new DocumentLineDto(l.Id, l.ProductId, l.Product.Sku, l.Product.Name, l.Qty, l.Uom.PublicId,
                l.Uom.Code, l.BaseQty, l.CostFromAverage ? null : l.EnteredUnitCost, Math.Round(l.UnitCost, Money.UnitPriceDecimals), l.Amount, l.CostFromAverage, l.Remark, null)).ToList(),
            new List<string>(), new List<ApprovalTriggerDto>(), d.RowVersion, d.WarehouseId, d.CreatedBy, d.SubmittedBy));
    }
}
