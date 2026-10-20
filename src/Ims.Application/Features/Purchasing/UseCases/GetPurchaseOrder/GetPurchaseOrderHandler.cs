namespace Ims.Application.Features.Purchasing;

public class GetPurchaseOrderHandler(IPurchaseOrderReader reader) : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>
{
    public Task<PurchaseOrderDto> HandleAsync(GetPurchaseOrderQuery q, CancellationToken ct) => reader.GetAsync(q.Id, ct);
}
