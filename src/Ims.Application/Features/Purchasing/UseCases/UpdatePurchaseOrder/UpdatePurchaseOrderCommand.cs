namespace Ims.Application.Features.Purchasing;

public sealed record UpdatePurchaseOrderCommand(Guid Id, PurchaseOrderSaveRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
