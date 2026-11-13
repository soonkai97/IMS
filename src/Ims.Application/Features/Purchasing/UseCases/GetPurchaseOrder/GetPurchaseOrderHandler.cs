namespace Ims.Application.Features.Purchasing;

public sealed class GetPurchaseOrderHandler(IPurchaseOrderReader reader) : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>
{
    public Task<PurchaseOrderDto> HandleAsync(GetPurchaseOrderQuery q, CancellationToken ct) => reader.GetAsync(q.Id, ct);
}
