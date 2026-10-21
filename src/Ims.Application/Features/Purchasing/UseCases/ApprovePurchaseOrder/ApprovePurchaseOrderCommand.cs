namespace Ims.Application.Features.Purchasing;

public record ApprovePurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
