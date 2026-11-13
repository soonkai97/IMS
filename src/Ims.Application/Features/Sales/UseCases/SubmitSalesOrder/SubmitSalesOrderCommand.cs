namespace Ims.Application.Features.Sales;

public sealed record SubmitSalesOrderCommand(Guid Id, string? IfMatch = null) : ICommand<SalesOrderDto>;
