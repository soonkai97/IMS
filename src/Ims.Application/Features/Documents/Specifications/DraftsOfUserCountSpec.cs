using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Drafts created by one user (optionally only the ones a reviewer sent back). Served by the (Status, Type, Id) index: drafts are few.</summary>
public sealed class DraftsOfUserCountSpec : Specification<StockDocument>
{
    public DraftsOfUserCountSpec(int userId, IReadOnlyCollection<int>? scope, bool returnedOnly)
    {
        Query.AsNoTracking().Where(d => d.Status == DocumentStatus.Draft && d.CreatedBy == userId);
        if (returnedOnly) Query.Where(d => d.RejectionReason != null);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId));
    }
}
