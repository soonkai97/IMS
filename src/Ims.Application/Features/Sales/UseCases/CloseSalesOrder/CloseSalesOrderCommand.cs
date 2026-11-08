namespace Ims.Application.Features.Sales;

public record CloseSalesOrderCommand(Guid Id, CloseSalesOrderRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
