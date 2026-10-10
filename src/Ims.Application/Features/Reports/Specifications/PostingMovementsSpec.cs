using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>The first <c>take</c> posting rows in report order (a page needs offset + size of them from each branch).</summary>
public sealed class PostingMovementsSpec : Specification<StockDocumentLine, MovementEntry>
{
    public PostingMovementsSpec(MovementFilter f, IReadOnlyCollection<int>? scope, int take)
    {
        MovementFilters.Apply(Query, f, scope);
        MovementOrder.ApplyToPostings(Query);
        Query.Take(take).Select(MovementProjection.Posting);
    }
}
