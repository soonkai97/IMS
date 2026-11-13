namespace Ims.Application.Features.Stock;

public sealed record QueryLedgerQuery(LedgerFilter Filter) : IQuery<PagedResult<LedgerRow>>;
