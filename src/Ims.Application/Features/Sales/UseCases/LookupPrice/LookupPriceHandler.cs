using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// The price an order line would be prefilled with. Read only: it writes nothing and audits nothing (the audit is written when the order is saved). The customer must be one an order can be saved for
/// (an active partner of type Customer, the rule of <see cref="SalesOrder.Create"/>), so the prefill never offers a price for an order the save would refuse; any other partner is the generic "Partner does not exist" (400).
/// </summary>
public class LookupPriceHandler(IReadRepository<Product> products, IReadRepository<Partner> partners, IIdMap<Uom> uomIds, IPriceLookup lookup) : IQueryHandler<LookupPriceQuery, PriceLookupDto>
{
    public async Task<PriceLookupDto> HandleAsync(LookupPriceQuery q, CancellationToken ct)
    {
        var f = q.Filter;
        var customer = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(f.CustomerId, tracked: false), ct) ?? throw new ValidationException(Messages.PartnerMissing);
        // Not a customer an order can be saved for (a supplier, an inactive partner): answered like a partner that does not exist, so the lookup says nothing about partners the caller did not ask about.
        if (!customer.IsUsableFor(DocumentType.Issue)) throw new ValidationException(Messages.PartnerMissing);
        var product = await products.FirstOrDefaultAsync(new ByPublicIdSpec<Product>(f.ProductId, tracked: false), ct) ?? throw new ValidationException(Messages.ProductMissing(f.ProductId));

        // A unit that does not exist is the id of no row: nothing is priced in it.
        var uom = f.UomId is { } unit ? (await uomIds.ToInternalAsync([unit], ct)).GetValueOrDefault(unit, IdMapExtensions.NoRow) : product.BaseUomId;
        var key = (product.Id, uom);
        var found = await lookup.FindAsync(customer.Id, [key], ct);
        return found.TryGetValue(key, out var price) ? new PriceLookupDto(true, price.Price, price.Source) : PriceLookupDto.None;
    }
}
