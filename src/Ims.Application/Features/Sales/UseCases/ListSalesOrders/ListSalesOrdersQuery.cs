namespace Ims.Application.Features.Sales;

public record ListSalesOrdersQuery(SalesOrderFilter Filter) : IQuery<PagedResult<SalesOrderListItem>>;
