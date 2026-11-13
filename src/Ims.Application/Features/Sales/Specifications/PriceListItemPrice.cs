namespace Ims.Application.Features.Sales;

public sealed record PriceListItemPrice(int ProductId, int UomId, decimal ListPrice);
