using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Documents posted in [<c>fromUtc</c>, <c>toUtc</c>): a seek on the filtered IX_StockDocument_PostedAt index. A document reversed later still counts as posted.</summary>
public sealed class PostedBetweenCountSpec : Specification<StockDocument>
{
    public PostedBetweenCountSpec(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(d => d.PostedAt >= fromUtc && d.PostedAt < toUtc);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId));
    }
}
