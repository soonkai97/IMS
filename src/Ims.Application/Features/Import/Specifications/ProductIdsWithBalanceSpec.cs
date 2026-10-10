using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>
/// Products that already have stock history. A balance row is created by a product's first posting, so this answers
/// "has movements" with one small query instead of scanning the (very large) ledger.
/// </summary>
public sealed class ProductIdsWithBalanceSpec : Specification<StockBalance, int>
{
    public ProductIdsWithBalanceSpec(IReadOnlyCollection<int> productIds) =>
        Query.AsNoTracking().Where(b => productIds.Contains(b.ProductId)).Select(b => b.ProductId);
}
