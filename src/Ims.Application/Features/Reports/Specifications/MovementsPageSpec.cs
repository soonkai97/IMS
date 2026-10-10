using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>One page of the receipt/issue detail, newest first (RPT-01).</summary>
public sealed class MovementsPageSpec : Specification<StockDocumentLine, MovementRow>
{
    public MovementsPageSpec(MovementFilter f, IReadOnlyCollection<int>? scope)
    {
        MovementFilters.Apply(Query, f, scope);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderByDescending(l => l.Document.PostedAt).ThenBy(l => l.Id).Skip((page - 1) * size).Take(size)
            .Select(MovementProjection.Expression);
    }
}
