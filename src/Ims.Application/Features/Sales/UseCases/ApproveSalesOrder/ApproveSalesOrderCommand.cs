namespace Ims.Application.Features.Sales;

public sealed record ApproveSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
