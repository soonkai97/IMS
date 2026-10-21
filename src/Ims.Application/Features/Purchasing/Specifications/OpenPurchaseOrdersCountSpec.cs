using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>Orders that are still open: Draft, Submitted or Approved (the first three states, so one range on the status index).</summary>
public sealed class OpenPurchaseOrdersCountSpec : Specification<PurchaseOrder>
{
    public OpenPurchaseOrdersCountSpec() => Query.AsNoTracking().Where(o => o.Status <= PurchaseOrderStatus.Approved);
}
