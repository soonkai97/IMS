using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The price an order line would be prefilled with. Read only: it writes nothing and audits nothing (the audit is written when the order is saved).</summary>
public class LookupPriceHandler(IReadRepository<Product> products, IIdMap<Partner> partnerIds, IIdMap<Uom> uomIds, IPriceLookup lookup) : IQueryHandler<LookupPriceQuery, PriceLookupDto>
{
    public async Task<PriceLookupDto> HandleAsync(LookupPriceQuery q, CancellationToken ct)
    {
        var f = q.Filter;
        var customer = (await partnerIds.ToInternalAsync([f.CustomerId], ct)).GetValueOrDefault(f.CustomerId);
        if (customer == 0) throw new ValidationException(Messages.PartnerMissing);
        var product = await products.FirstOrDefaultAsync(new ByPublicIdSpec<Product>(f.ProductId, tracked: false), ct) ?? throw new ValidationException(Messages.ProductMissing(f.ProductId));

        // A unit that does not exist is the id of no row: nothing is priced in it.
        var uom = f.UomId is { } unit ? (await uomIds.ToInternalAsync([unit], ct)).GetValueOrDefault(unit, IdMapExtensions.NoRow) : product.BaseUomId;
        var key = (product.Id, uom);
        var found = await lookup.FindAsync(customer, [key], ct);
        return found.TryGetValue(key, out var price) ? new PriceLookupDto(true, price.Price, price.Source) : PriceLookupDto.None;
    }
}
