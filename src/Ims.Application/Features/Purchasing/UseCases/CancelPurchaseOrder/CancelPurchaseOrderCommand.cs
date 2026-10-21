using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

public record CancelPurchaseOrderCommand(Guid Id, CancelRequest Data, string? IfMatch = null) : ICommand<PurchaseOrderDto>;
