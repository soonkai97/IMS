namespace Ims.Application.Features.Purchasing;

public sealed record ApprovePurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
