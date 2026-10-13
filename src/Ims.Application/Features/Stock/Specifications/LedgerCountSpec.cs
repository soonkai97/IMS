using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class LedgerCountSpec : Specification<StockLedgerEntry>
{
    public LedgerCountSpec(LedgerFilter f, int? warehouseId, int? productId, int? sourceDocId, IReadOnlyCollection<int>? scope) => LedgerFilters.Apply(Query, f, warehouseId, productId, sourceDocId, scope);
}
