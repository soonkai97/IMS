namespace Ims.Application.Features.Purchasing;

public record CreatePurchaseOrderCommand(PurchaseOrderSaveRequest Data) : ICommand<PurchaseOrderDto>;
