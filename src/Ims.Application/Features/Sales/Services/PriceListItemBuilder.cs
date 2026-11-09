using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Loads the products and units a price list save names (public ids, one query each for the whole list); the items themselves (usable product, convertible unit, price limits) are built by the domain.</summary>
public class PriceListItemBuilder(IReadRepository<Product> products, IReadRepository<Uom> uoms, IIdMap<Uom> uomIds) : IPriceListItemBuilder, IAppService
{
    public async Task<BuiltPriceListItems> BuildAsync(IReadOnlyList<PriceListItemInput> inputs, IReadOnlySet<(int ProductId, int UomId)>? existing, CancellationToken ct)
    {
        var byId = (await products.ListAsync(new ProductsForLinesSpec(inputs.Select(i => i.ProductId).Distinct().ToList()), ct)).ToDictionary(p => p.PublicId);

        // The units a client names are public ids; a unit that does not exist becomes the id of no row, which the domain refuses like any unit the product cannot convert.
        var named = await uomIds.ToInternalAsync(inputs.Where(i => i.UomId is not null).Select(i => i.UomId!.Value), ct);
        var codeKeys = named.Values.Concat(byId.Values.Select(p => p.BaseUomId)).Distinct().ToList();
        var codes = (await uoms.ListAsync(new ByIdsSpec<Uom>(codeKeys, tracked: false), ct)).ToDictionary(u => u.Id, u => u.Code);

        var items = new List<PriceListItem>(inputs.Count);
        var seen = new HashSet<(int, int)>();
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            try
            {
                if (!byId.TryGetValue(input.ProductId, out var product)) throw new DomainException(Messages.ProductMissing(input.ProductId));
                int? uomId = input.UomId is { } unit ? named.GetValueOrDefault(unit, IdMapExtensions.NoRow) : null;
                var kept = existing is not null && existing.Contains((product.Id, uomId ?? product.BaseUomId));
                var item = PriceListItem.Create(product, uomId, input.Price, requireUsable: !kept);
                if (!seen.Add((item.ProductId, item.UomId))) throw new DomainException(Messages.PriceListDuplicateItem(product.Sku, codes.GetValueOrDefault(item.UomId, "")));
                items.Add(item);
            }
            catch (DomainException e)
            {
                errors[ErrorKeys.Normalize($"Data.Items[{i}]")] = [e.Message]; // the name the client sent: items[2]
            }
        }
        if (errors.Count > 0) throw new ValidationException(errors.First().Value[0], errors);
        return new BuiltPriceListItems(items, byId.Values.ToDictionary(p => p.Id, p => p.Sku), codes);
    }
}
