using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// The SKU of each product and the code of each unit a price-list save names, in ONE statement: the two small sets are joined end to end (<c>UNION ALL</c>), so a save pays one round trip however many items it
/// touches. Both read the primary key. Soft-deleted rows are not found (the global filter); the caller names what is missing as blank, as the audit always did.
/// </summary>
public sealed class EfPriceListLabelReader(AppDbContext db) : IPriceListLabelReader
{
    public async Task<PriceListLabels> ReadAsync(IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> uomIds, CancellationToken ct)
    {
        if (productIds.Count == 0 && uomIds.Count == 0) return new PriceListLabels(new Dictionary<int, string>(), new Dictionary<int, string>());

        var products = db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { IsProduct = true, Key = p.Id, Text = p.Sku });
        var uoms = db.Set<Uom>().AsNoTracking().Where(u => uomIds.Contains(u.Id)).Select(u => new { IsProduct = false, Key = u.Id, Text = u.Code });
        var rows = await products.Concat(uoms).ToListAsync(ct);

        return new PriceListLabels(
            rows.Where(r => r.IsProduct).ToDictionary(r => r.Key, r => r.Text),
            rows.Where(r => !r.IsProduct).ToDictionary(r => r.Key, r => r.Text));
    }
}
