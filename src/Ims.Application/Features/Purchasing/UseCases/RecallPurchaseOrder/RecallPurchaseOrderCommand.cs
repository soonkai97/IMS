namespace Ims.Application.Features.Purchasing;

public record RecallPurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
