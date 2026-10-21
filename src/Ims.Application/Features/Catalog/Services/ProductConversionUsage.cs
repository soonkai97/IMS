using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class ProductConversionUsage(IReadRepository<StockLedgerEntry> ledger, IReadRepository<PurchaseOrderLine> orderLines, IReadRepository<Uom> uoms) : IProductConversionUsage, IAppService
{
    private static readonly IReadOnlyDictionary<int, string> None = new Dictionary<int, string>();

    public async Task<IReadOnlyDictionary<int, string>> UnitsInUseAsync(int productId, IReadOnlyCollection<int> unitIds, CancellationToken ct)
    {
        if (productId == 0 || unitIds.Count == 0) return None;
        var used = unitIds;
        if (!await ledger.AnyAsync(new ProductHasMovementsSpec(productId), ct))
            used = (await orderLines.ListAsync(new OrderedUnitsOfProductSpec(productId, unitIds), ct)).Distinct().ToList();
        if (used.Count == 0) return None;
        return (await uoms.ListAsync(new UomsByIdsSpec(used), ct)).ToDictionary(u => u.Id, u => u.Code);
    }
}
