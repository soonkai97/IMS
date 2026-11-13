using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

public sealed record RejectPurchaseOrderCommand(Guid Id, RejectRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
