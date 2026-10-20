namespace Ims.Application.Features.Purchasing;

public record UpdatePurchaseOrderCommand(Guid Id, PurchaseOrderSaveRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
