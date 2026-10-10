using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>
/// MD-05: previews and validates every row, and writes only when <c>Commit</c> is true. Import is all-or-nothing (UX decision D6):
/// a commit with any invalid row is refused, and a valid commit is one transaction.
/// </summary>
public class ImportProductsHandler(IExcelReader excel, IRepository<Product> products, IRepository<Category> categories, IRepository<Uom> uoms,
    IReadRepository<ProductBarcode> barcodes, IReadRepository<StockBalance> balances, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<ImportProductsCommand, ImportResult>
{
    private record ProductRow(int Row, string Sku, string Name, string Category, string Unit, decimal Safety, List<string> Barcodes);

    public async Task<ImportResult> HandleAsync(ImportProductsCommand cmd, CancellationToken ct)
    {
        var sheet = excel.Read(cmd.File, Limits.MaxImportRows);
        ImportSheet.RequireColumns(sheet, ImportColumns.Sku, ImportColumns.Name);

        var (results, parsed) = await ValidateAsync(sheet, ct);
        if (!cmd.Commit)
            return new ImportResult(false, results.Count, results.Count(r => r.Ok), results.Count(r => !r.Ok), 0, 0, null, results);

        ImportSheet.EnsureNoErrors(results);
        var (created, updated) = await uow.ExecuteAsync(() => CommitAsync(parsed, results, ct), ct);
        return new ImportResult(true, results.Count, results.Count, 0, created, updated, null, results);
    }

    private async Task<(List<ImportRowResult> Results, List<ProductRow> Parsed)> ValidateAsync(ExcelSheet sheet, CancellationToken ct)
    {
        // Only the barcodes that appear in the file are looked up, not the whole catalogue.
        var fileCodes = sheet.Rows.SelectMany(r => Split(r.Get(ImportColumns.Barcodes))).Distinct().ToList();
        var owners = fileCodes.Count == 0 ? [] : await barcodes.ListAsync(new BarcodeOwnersSpec(fileCodes), ct);
        var ownerSkus = owners.Count == 0
            ? new Dictionary<int, string>()
            : (await products.ListAsync(new ProductSkusByIdsSpec(owners.Select(o => o.ProductId).Distinct().ToList()), ct)).ToDictionary(p => p.Id, p => p.Sku);
        var codeOwner = owners.ToDictionary(o => o.Barcode, o => ownerSkus.GetValueOrDefault(o.ProductId, ""));

        var results = new List<ImportRowResult>();
        var parsed = new List<ProductRow>();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCodes = new Dictionary<string, string>();

        foreach (var r in sheet.Rows)
        {
            var errors = new List<string>();
            var sku = r.Get(ImportColumns.Sku);
            var name = r.Get(ImportColumns.Name);
            var category = r.Get(ImportColumns.Category);
            var unit = r.Get(ImportColumns.Unit).ToUpperInvariant();
            if (unit.Length == 0) unit = ImportColumns.DefaultUnit;

            if (sku.Length == 0) errors.Add(Messages.SkuRequired);
            else if (!seenSkus.Add(sku)) errors.Add(Messages.DuplicateSkuInFile(sku));
            if (name.Length == 0) errors.Add(Messages.ProductNameRequired);
            ImportSheet.CheckLength(errors, "SKU", sku, Limits.SkuMaxLength);
            ImportSheet.CheckLength(errors, "Name", name, Limits.NameMaxLength);
            ImportSheet.CheckLength(errors, "Category", category, Limits.ShortNameMaxLength);
            ImportSheet.CheckLength(errors, "Unit", unit, Limits.CodeMaxLength);

            decimal safety = 0;
            if (r.Get(ImportColumns.SafetyStock) is { Length: > 0 } ss && !(ImportSheet.TryParseDecimal(ss, out safety) && safety >= 0))
                errors.Add(Messages.SafetyStockNegative);

            var codes = Split(r.Get(ImportColumns.Barcodes));
            foreach (var code in codes)
            {
                ImportSheet.CheckLength(errors, "Barcode", code, Limits.BarcodeMaxLength);
                if (codeOwner.TryGetValue(code, out var owner) && !string.Equals(owner, sku, StringComparison.OrdinalIgnoreCase))
                    errors.Add(Messages.BarcodeOwnedBy(code, owner));
                else if (seenCodes.TryGetValue(code, out var other) && !string.Equals(other, sku, StringComparison.OrdinalIgnoreCase))
                    errors.Add(Messages.BarcodeDuplicateInFile(code, other));
                else seenCodes[code] = sku;
            }

            results.Add(new ImportRowResult(r.Number, sku, errors.Count == 0, errors));
            if (errors.Count == 0) parsed.Add(new ProductRow(r.Number, sku, name, category, unit, safety, codes));
        }
        return (results, parsed);
    }

    private async Task<(int Created, int Updated)> CommitAsync(List<ProductRow> parsed, List<ImportRowResult> results, CancellationToken ct)
    {
        var cats = (await categories.ListAsync(ct)).ToDictionary(c => c.Name.ToLowerInvariant());
        var units = (await uoms.ListAsync(ct)).ToDictionary(u => u.Code);
        var existing = (await products.ListAsync(new ProductsBySkusSpec(parsed.Select(p => p.Sku).ToList()), ct)).ToDictionary(p => p.Sku.ToLowerInvariant());
        var withStock = (await balances.ListAsync(new ProductIdsWithBalanceSpec(existing.Values.Select(p => p.Id).ToList()), ct)).ToHashSet();
        int created = 0, updated = 0;
        var resultIndex = results.Select((r, i) => (r.Row, i)).ToDictionary(x => x.Row, x => x.i); // O(1) lookups below

        foreach (var row in parsed)
        {
            Category? cat = null;
            if (row.Category.Length > 0 && !cats.TryGetValue(row.Category.ToLowerInvariant(), out cat))
            {
                cat = Category.Create(row.Category);
                categories.Add(cat);
                cats[row.Category.ToLowerInvariant()] = cat;
            }
            if (!units.TryGetValue(row.Unit, out var uom))
            {
                uom = Uom.Create(row.Unit);
                uoms.Add(uom);
                units[row.Unit] = uom;
            }

            if (!existing.TryGetValue(row.Sku.ToLowerInvariant(), out var p))
            {
                p = Product.Create(row.Sku, row.Name, cat, uom, row.Safety);
                products.Add(p);
                created++;
            }
            else
            {
                p.Update(row.Sku, row.Name, cat, row.Safety, p.IsActive);
                // The aggregate refuses a unit change once stock exists (BR-04); the import keeps the old unit and tells the user.
                if (p.BaseUomId != uom.Id)
                {
                    if (withStock.Contains(p.Id))
                        results[resultIndex[row.Row]] = new ImportRowResult(row.Row, row.Sku, true, [Messages.UnitNotChanged]);
                    else
                        p.ChangeBaseUnit(uom, hasStockMovements: false);
                }
                updated++;
            }
            p.SetBarcodes(p.Barcodes.Select(b => b.Barcode).Concat(row.Barcodes));
        }

        audit.Record(AuditActions.ImportProducts, AuditEntities.Product, AuditEntities.Bulk, after: new { created, updated });
        await uow.SaveChangesAsync(ct);
        return (created, updated);
    }

    private static List<string> Split(string text) =>
        text.Split(ImportColumns.BarcodeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
}
