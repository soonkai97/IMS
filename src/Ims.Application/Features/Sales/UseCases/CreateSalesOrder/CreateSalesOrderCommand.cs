namespace Ims.Application.Features.Sales;

public record CreateSalesOrderCommand(SalesOrderSaveRequest Data) : ICommand<SalesOrderDto>;
