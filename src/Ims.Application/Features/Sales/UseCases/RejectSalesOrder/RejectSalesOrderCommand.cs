using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

public sealed record RejectSalesOrderCommand(Guid Id, RejectRequest Data, string? IfMatch = null) : ICommand<SalesOrderDto>;
