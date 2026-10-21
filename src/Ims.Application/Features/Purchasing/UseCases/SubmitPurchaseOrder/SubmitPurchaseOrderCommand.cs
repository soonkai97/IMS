namespace Ims.Application.Features.Purchasing;

public record SubmitPurchaseOrderCommand(Guid Id, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
