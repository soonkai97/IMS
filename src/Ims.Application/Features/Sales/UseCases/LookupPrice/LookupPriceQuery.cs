namespace Ims.Application.Features.Sales;

public sealed record LookupPriceQuery(PriceLookupFilter Filter) : IQuery<PriceLookupDto>;
