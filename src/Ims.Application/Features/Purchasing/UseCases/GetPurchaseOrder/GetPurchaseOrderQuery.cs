namespace Ims.Application.Features.Purchasing;

public record GetPurchaseOrderQuery(Guid Id) : IQuery<PurchaseOrderDto>;
