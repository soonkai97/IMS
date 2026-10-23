namespace Ims.Application.Features.Sales;

public record GetSalesOrderQuery(Guid Id) : IQuery<SalesOrderDto>;
