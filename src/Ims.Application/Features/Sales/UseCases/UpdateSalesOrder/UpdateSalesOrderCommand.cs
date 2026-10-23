namespace Ims.Application.Features.Sales;

public record UpdateSalesOrderCommand(Guid Id, SalesOrderSaveRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
