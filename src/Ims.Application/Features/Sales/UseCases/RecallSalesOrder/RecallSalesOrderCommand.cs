namespace Ims.Application.Features.Sales;

public sealed record RecallSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
