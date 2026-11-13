namespace Ims.Application.Features.Purchasing;

public sealed record RecallPurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
