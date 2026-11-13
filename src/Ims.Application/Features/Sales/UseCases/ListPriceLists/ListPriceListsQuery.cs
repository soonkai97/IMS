namespace Ims.Application.Features.Sales;

public sealed record ListPriceListsQuery(PriceListFilter Filter) : IQuery<PagedResult<PriceListListItem>>;
