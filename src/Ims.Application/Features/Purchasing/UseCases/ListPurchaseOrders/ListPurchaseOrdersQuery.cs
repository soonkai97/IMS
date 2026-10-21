namespace Ims.Application.Features.Purchasing;

public record ListPurchaseOrdersQuery(PurchaseOrderFilter Filter) : IQuery<PagedResult<PurchaseOrderListItem>>;
