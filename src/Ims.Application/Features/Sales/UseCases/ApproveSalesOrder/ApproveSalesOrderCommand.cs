namespace Ims.Application.Features.Sales;

public record ApproveSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
