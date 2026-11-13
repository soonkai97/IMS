namespace Ims.Application.Features.Purchasing;

public sealed record SubmitPurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
