using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

public sealed record CancelSalesOrderCommand(Guid Id, CancelRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
