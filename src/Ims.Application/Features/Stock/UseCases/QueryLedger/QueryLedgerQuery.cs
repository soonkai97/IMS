namespace Ims.Application.Features.Stock;

public record QueryLedgerQuery(LedgerFilter Filter) : IQuery<PagedResult<LedgerRow>>;
