using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Orders that are still open: Draft, Submitted or Approved (listed one by one, so a new state is never counted by accident; the status index answers each).</summary>
public sealed class OpenSalesOrdersCountSpec : Specification<SalesOrder>
{
    public OpenSalesOrdersCountSpec() => Query.AsNoTracking().Where(o => o.Status == SalesOrderStatus.Draft || o.Status == SalesOrderStatus.Submitted || o.Status == SalesOrderStatus.Approved);
}
