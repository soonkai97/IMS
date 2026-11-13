namespace Ims.Application.Features.Purchasing;

public sealed record ListPurchaseOrdersQuery(PurchaseOrderFilter Filter) : IQuery<PagedResult<PurchaseOrderListItem>>;
