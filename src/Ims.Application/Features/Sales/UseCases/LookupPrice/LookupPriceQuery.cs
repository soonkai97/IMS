namespace Ims.Application.Features.Sales;

public record LookupPriceQuery(PriceLookupFilter Filter) : IQuery<PriceLookupDto>;
