namespace Ims.Application.Features.Sales;

public sealed record GetSalesOrderQuery(Guid Id) : IQuery<SalesOrderDto>;
