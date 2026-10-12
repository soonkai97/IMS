using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

public sealed class EfMovementReader(AppDbContext db) : IMovementReader
{
    public async Task<int> CountAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, CancellationToken ct) =>
        await PerProduct(Evaluate(new MovementsCountSpec(filter, warehouseId, productId, warehouseScope)), productId).CountAsync(ct)
        + await PerProduct(Evaluate(new ReversalMovementsCountSpec(filter, warehouseId, productId, warehouseScope)), productId).CountAsync(ct);

    public Task<IReadOnlyList<MovementRow>> PageAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(filter.Page, filter.PageSize);
        return RowsAsync(filter, warehouseId, productId, warehouseScope, skip: (page - 1) * size, size, ct);
    }

    public Task<IReadOnlyList<MovementRow>> ListAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, int limit, CancellationToken ct) =>
        RowsAsync(filter, warehouseId, productId, warehouseScope, skip: 0, limit, ct);

    /// <summary>
    /// Each branch is cut to its first skip + size rows in its own order (an index scan for the postings), then the two are merged and the page is
    /// cut out of the merge. A branch never needs more than that: the page cannot reach past it.
    /// </summary>
    private async Task<IReadOnlyList<MovementRow>> RowsAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? scope, int skip, int size, CancellationToken ct)
    {
        var take = skip + size;
        var postings = Evaluate(new PostingMovementsSpec(filter, warehouseId, productId, scope, take));
        var reversals = Evaluate(new ReversalMovementsSpec(filter, warehouseId, productId, scope, take));
        var entries = await PerProduct(MovementOrder.Merged(postings.Concat(reversals)).Skip(skip).Take(size), productId).ToListAsync(ct);
        return entries.Select(e => e.ToRow()).ToList();
    }

    /// <summary>
    /// A product filter gets a plan of its own (see <see cref="RecompileInterceptor"/>): the plan for a product with thousands of lines walks all
    /// documents when the product has few or none, and the plan cache would otherwise hand the first product's plan to every other product.
    /// </summary>
    private static IQueryable<T> PerProduct<T>(IQueryable<T> query, int? productId) =>
        productId is null ? query : query.TagWith(RecompileInterceptor.Tag);

    private IQueryable<T> Evaluate<T>(ISpecification<StockDocumentLine, T> spec) => SpecificationEvaluator.Default.GetQuery(db.Set<StockDocumentLine>(), spec);

    private IQueryable<StockDocumentLine> Evaluate(ISpecification<StockDocumentLine> spec) => SpecificationEvaluator.Default.GetQuery(db.Set<StockDocumentLine>(), spec);
}
