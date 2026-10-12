using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>
/// Orchestrates saving a product: looks up what must come from the database (unit, category, uniqueness, stock history),
/// then lets the <see cref="Product"/> aggregate apply and guard the change.
/// </summary>
public class ProductWriter(IRepository<Product> products, IReadRepository<Uom> uoms, IReadRepository<Category> categories,
    IReadRepository<ProductBarcode> barcodes, IReadRepository<StockLedgerEntry> ledger, IIdMap<Uom> uomIds, IUnitOfWork uow) : IProductWriter, IAppService
{
    public async Task<int> SaveAsync(int? id, ProductSaveRequest r, CancellationToken ct)
    {
        var sku = r.Sku.Trim();
        var exceptId = id ?? 0;
        var baseUom = await uoms.FirstOrDefaultAsync(new ByPublicIdSpec<Uom>(r.BaseUomId), ct) ?? throw new ValidationException(Messages.BaseUomMissing);
        Category? category = null;
        if (r.CategoryId is { } cid)
            category = await categories.FirstOrDefaultAsync(new ByPublicIdSpec<Category>(cid), ct) ?? throw new ValidationException(Messages.CategoryMissing);

        if (await products.AnyAsync(new SkuTakenSpec(sku, exceptId), ct)) throw new ConflictException(Messages.SkuExists(sku));
        var codes = (r.Barcodes ?? []).Where(b => b is not null).Select(b => b.Trim()).Where(b => b.Length > 0).Distinct().ToList();
        var clash = await barcodes.FirstOrDefaultAsync(new BarcodeTakenSpec(codes, exceptId), ct);
        if (clash is not null) throw new ConflictException(Messages.BarcodeTaken(clash.Barcode));

        Product product;
        if (id is null)
        {
            product = Product.Create(sku, r.Name, category, baseUom, r.SafetyStock, r.IsActive);
            products.Add(product);
        }
        else
        {
            product = await products.FirstOrDefaultAsync(new ProductForEditSpec(id.Value), ct) ?? throw new NotFoundException(Messages.ProductNotFound);
            product.Update(sku, r.Name, category, r.SafetyStock, r.IsActive);
            if (product.BaseUomId != baseUom.Id)
                product.ChangeBaseUnit(baseUom, await ledger.AnyAsync(new ProductHasMovementsSpec(product.Id), ct));
        }

        product.SetBarcodes(codes);
        if (r.Conversions is not null)
        {
            var wanted = r.Conversions.SelectMany(c => new[] { c.FromUomId, c.ToUomId }).Distinct().ToList();
            var units = await uomIds.ToInternalAsync(wanted, ct);
            if (units.Count != wanted.Count) throw new ValidationException(Messages.ConversionInvalid); // a unit that does not exist
            product.SetConversions(r.Conversions.Select(c => new ConversionDefinition(units[c.FromUomId], units[c.ToUomId], c.Factor)));
        }

        await uow.SaveChangesAsync(ct);
        return product.Id;
    }
}
