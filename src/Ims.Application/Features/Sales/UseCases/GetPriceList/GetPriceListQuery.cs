namespace Ims.Application.Features.Sales;

public sealed record GetPriceListQuery(Guid Id) : IQuery<PriceListDto>;
