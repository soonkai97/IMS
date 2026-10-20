using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Ims.Application.Features.Accounting;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Each of the two branches (postings, reversals) is read in its own order and cut to <c>limit</c> rows, and the two are merged and cut again, in one statement. A branch never needs more
/// than <c>limit</c> rows: if it has more, the file is already over the cap. The date predicates are the movements report's, so the filtered PostedAt and ReversedAt indexes are available to the optimizer (at 18% of the documents the measured plan scans, at the
/// 1M tier a month is about 5%: docs/dba/README.md section 11), and the rows are projected to the file's columns in the same statement (no row loads an entity and then its product and partner one by one).
/// </summary>
public sealed class EfAccountingReader(AppDbContext db) : IAccountingReader
{
    public async Task<IReadOnlyList<AccountingEntry>> ListAsync(DateOnly from, DateOnly to, int? warehouseId, IReadOnlyCollection<int>? warehouseScope, int limit, CancellationToken ct)
    {
        var postings = Evaluate(new AccountingPostingsSpec(from, to, warehouseId, warehouseScope, limit));
        var reversals = Evaluate(new AccountingReversalsSpec(from, to, warehouseId, warehouseScope, limit));
        return await AccountingOrder.Merged(postings.Concat(reversals)).Take(limit).ToListAsync(ct);
    }

    private IQueryable<AccountingEntry> Evaluate(ISpecification<StockDocumentLine, AccountingEntry> spec) =>
        SpecificationEvaluator.Default.GetQuery(db.Set<StockDocumentLine>(), spec);
}
