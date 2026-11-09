namespace Ims.Application.Features.Sales;

public record ListPriceListsQuery(PriceListFilter Filter) : IQuery<PagedResult<PriceListListItem>>;
