namespace Ims.Application.Features.Sales;

public record GetPriceListQuery(Guid Id) : IQuery<PriceListDto>;
