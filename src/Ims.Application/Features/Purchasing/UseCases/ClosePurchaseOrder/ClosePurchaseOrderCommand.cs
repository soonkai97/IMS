namespace Ims.Application.Features.Purchasing;

public sealed record ClosePurchaseOrderCommand(Guid Id, CloseRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
