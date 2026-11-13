namespace Ims.Application.Features.Sales;

public sealed record ListSalesOrdersQuery(SalesOrderFilter Filter) : IQuery<PagedResult<SalesOrderListItem>>;
