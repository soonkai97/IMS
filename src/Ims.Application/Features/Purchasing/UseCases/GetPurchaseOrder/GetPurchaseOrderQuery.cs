namespace Ims.Application.Features.Purchasing;

public sealed record GetPurchaseOrderQuery(Guid Id) : IQuery<PurchaseOrderDto>;
