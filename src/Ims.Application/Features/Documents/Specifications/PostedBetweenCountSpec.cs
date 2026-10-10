using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// Documents posted in [<c>fromUtc</c>, <c>toUtc</c>) that are still Posted: a seek on the filtered IX_StockDocument_PostedAt index (Status is one of its
/// included columns). A document reversed later is no longer counted, so the Home tile equals the Posted list for the day.
/// </summary>
public sealed class PostedBetweenCountSpec : Specification<StockDocument>
{
    public PostedBetweenCountSpec(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(d => d.PostedAt >= fromUtc && d.PostedAt < toUtc && d.Status == DocumentStatus.Posted);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId));
    }
}
