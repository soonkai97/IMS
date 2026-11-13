namespace Ims.Application.Features.Sales;

public sealed record CreateSalesOrderCommand(SalesOrderSaveRequest Data) : ICommand<SalesOrderDto>;
