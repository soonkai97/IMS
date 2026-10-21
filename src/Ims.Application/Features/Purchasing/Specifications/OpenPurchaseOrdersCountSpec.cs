using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>Orders that are still open: Draft, Submitted or Approved (listed one by one, so a new state is never counted by accident; the status index answers each).</summary>
public sealed class OpenPurchaseOrdersCountSpec : Specification<PurchaseOrder>
{
    public OpenPurchaseOrdersCountSpec() => Query.AsNoTracking().Where(o => o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Submitted || o.Status == PurchaseOrderStatus.Approved);
}
