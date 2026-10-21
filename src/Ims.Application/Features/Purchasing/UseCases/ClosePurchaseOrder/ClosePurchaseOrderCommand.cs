namespace Ims.Application.Features.Purchasing;

public record ClosePurchaseOrderCommand(Guid Id, CloseRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
