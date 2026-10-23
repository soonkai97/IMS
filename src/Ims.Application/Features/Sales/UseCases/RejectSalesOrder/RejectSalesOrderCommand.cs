using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

public record RejectSalesOrderCommand(Guid Id, RejectRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
