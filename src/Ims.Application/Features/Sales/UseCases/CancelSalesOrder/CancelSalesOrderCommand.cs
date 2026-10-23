using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

public record CancelSalesOrderCommand(Guid Id, CancelRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
