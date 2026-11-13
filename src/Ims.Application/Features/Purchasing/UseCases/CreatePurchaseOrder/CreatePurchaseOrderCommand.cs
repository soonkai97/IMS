namespace Ims.Application.Features.Purchasing;

public sealed record CreatePurchaseOrderCommand(PurchaseOrderSaveRequest Data) : ICommand<PurchaseOrderDto>;
