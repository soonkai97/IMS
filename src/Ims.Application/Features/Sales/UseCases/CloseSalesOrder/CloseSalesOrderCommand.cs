namespace Ims.Application.Features.Sales;

public sealed record CloseSalesOrderCommand(Guid Id, CloseSalesOrderRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
