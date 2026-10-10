using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Stock;

public sealed class LedgerCountSpec : Specification<StockLedgerEntry>
{
    public LedgerCountSpec(LedgerFilter f, IReadOnlyCollection<int>? scope) => LedgerFilters.Apply(Query, f, scope);
}
