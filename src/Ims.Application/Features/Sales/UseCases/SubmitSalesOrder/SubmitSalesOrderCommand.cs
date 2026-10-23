namespace Ims.Application.Features.Sales;

public record SubmitSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
