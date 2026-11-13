namespace Ims.Application.Features.Sales;

public sealed record UpdateSalesOrderCommand(Guid Id, SalesOrderSaveRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
