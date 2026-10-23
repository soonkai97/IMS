namespace Ims.Application.Features.Sales;

public record RecallSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
