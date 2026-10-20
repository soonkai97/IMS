using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>
/// MD-05: previews and validates every row, and writes only when <c>Commit</c> is true. Import is all-or-nothing (UX decision D6):
/// a commit with any invalid row is refused, and a valid commit is one transaction. A category or unit that does not exist is a row error: the
/// import never creates master data on the side (one typo must not add a category), an admin adds them first.
/// The optional HsCode and SstCode columns (accounting module) follow the API: a file that carries one while the module is off is refused; with the module on a column in the file
/// replaces that field of every row it names (a blank cell clears it, like a blank Category), and a column that is not in the file leaves the stored values alone.
/// </summary>
public class ImportProductsHandler(IExcelReader excel, IRepository<Product> products, IRepository<Category> categories, IRepository<Uom> uoms,
    IReadRepository<ProductBarcode> barcodes, IReadRepository<StockBalance> balances, IModuleSwitches modules, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<ImportProductsCommand, ImportResult>
{
    private record ProductRow(int Row, string Sku, string Name, int? CategoryId, int UomId, decimal Safety, List<string> Barcodes, string? HsCode, string? SstCode);

    /// <summary>Which of the optional tax columns the file has (always false while the accounting module is off: such a file is refused).</summary>
    private record TaxColumns(bool HsCode, bool SstCode);

    public async Task<ImportResult> HandleAsync(ImportProductsCommand cmd, CancellationToken ct)
    {
        var sheet = excel.Read(cmd.File, Limits.MaxImportRows);
        ImportSheet.RequireColumns(sheet, ImportColumns.Sku, ImportColumns.Name);

        var tax = await TaxColumnsOfAsync(sheet, ct);
        var (results, parsed) = await ValidateAsync(sheet, tax, ct);
        if (!cmd.Commit)
            return new ImportResult(false, results.Count, results.Count(r => r.Ok), results.Count(r => !r.Ok),
                results.Count(r => r.Status == ImportRowStatus.New), results.Count(r => r.Status == ImportRowStatus.Update), null, results);

        ImportSheet.EnsureNoErrors(results);
        var (created, updated) = await uow.ExecuteAsync(() => CommitAsync(parsed, results, tax, ct), ct);
        return new ImportResult(true, results.Count, results.Count, 0, created, updated, null, results);
    }

    private async Task<TaxColumns> TaxColumnsOfAsync(ExcelSheet sheet, CancellationToken ct)
    {
        var present = new TaxColumns(sheet.Headers.Contains(ImportColumns.HsCode), sheet.Headers.Contains(ImportColumns.SstCode));
        if (!present.HsCode && !present.SstCode) return present;
        if (await modules.IsEnabledAsync(ModuleCodes.Accounting, ct)) return present;
        var named = new[] { (present.HsCode, "HsCode"), (present.SstCode, "SstCode") }.Where(c => c.Item1).Select(c => c.Item2);
        throw new ValidationException(Messages.ImportColumnsNotAvailable(named, ModuleCodes.Accounting));
    }

    private async Task<(List<ImportRowResult> Results, List<ProductRow> Parsed)> ValidateAsync(ExcelSheet sheet, TaxColumns tax, CancellationToken ct)
    {
        // Only the barcodes that appear in the file are looked up, not the whole catalogue.
        var fileCodes = sheet.Rows.SelectMany(r => Split(r.Get(ImportColumns.Barcodes))).Distinct().ToList();
        var owners = fileCodes.Count == 0 ? [] : await barcodes.ListAsync(new BarcodeOwnersSpec(fileCodes), ct);
        var ownerSkus = owners.Count == 0
            ? new Dictionary<int, string>()
            : (await products.ListAsync(new ProductSkusByIdsSpec(owners.Select(o => o.ProductId).Distinct().ToList()), ct)).ToDictionary(p => p.Id, p => p.Sku);
        var codeOwner = owners.ToDictionary(o => o.Barcode, o => ownerSkus.GetValueOrDefault(o.ProductId, ""));

        // One query for the SKUs in the file tells which rows update an existing product and which create a new one.
        var fileSkus = sheet.Rows.Select(r => r.Get(ImportColumns.Sku)).Where(s => s.Length > 0).Distinct().ToList();
        var existingSkus = fileSkus.Count == 0
            ? new HashSet<string>()
            : (await products.ListAsync(new ProductSkusBySkusSpec(fileSkus), ct)).Select(p => p.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Categories and units are small lookup lists (hard cap 1,000): read once, matched in memory. A unit matches by code or by name.
        var categoryIds = (await categories.ListAsync(ct)).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var unitList = await uoms.ListAsync(ct);
        var unitIds = unitList.GroupBy(u => u.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var unitIdsByName = unitList.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var results = new List<ImportRowResult>();
        var parsed = new List<ProductRow>();
        var firstRowOfSku = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seenCodes = new Dictionary<string, string>();

        foreach (var r in sheet.Rows)
        {
            var errors = new List<string>();
            var sku = r.Get(ImportColumns.Sku);
            var name = r.Get(ImportColumns.Name);
            var category = r.Get(ImportColumns.Category);
            var unit = r.Get(ImportColumns.Unit);
            if (unit.Length == 0) unit = ImportColumns.DefaultUnit;

            if (sku.Length == 0) errors.Add(Messages.SkuRequired);
            else if (!firstRowOfSku.TryAdd(sku, r.Number)) errors.Add(Messages.DuplicateSkuInFile(sku, firstRowOfSku[sku]));
            if (name.Length == 0) errors.Add(Messages.ProductNameRequired);
            ImportSheet.CheckLength(errors, "SKU", sku, Limits.SkuMaxLength);
            ImportSheet.CheckLength(errors, "Name", name, Limits.NameMaxLength);
            ImportSheet.CheckLength(errors, "Category", category, Limits.ShortNameMaxLength);
            ImportSheet.CheckLength(errors, "Unit", unit, Limits.CodeMaxLength);

            int? categoryId = null;
            if (category.Length > 0)
            {
                if (categoryIds.TryGetValue(category, out var foundCategory)) categoryId = foundCategory;
                else errors.Add(Messages.ImportCategoryMissing(category));
            }
            var unitId = 0;
            if (unitIds.TryGetValue(unit, out var foundUnit) || unitIdsByName.TryGetValue(unit, out foundUnit)) unitId = foundUnit;
            else errors.Add(Messages.ImportUnitMissing(unit));

            decimal safety = 0;
            if (r.GetNumber(ImportColumns.SafetyStock) is { Length: > 0 } ss)
            {
                if (!ImportSheet.TryParseDecimal(ss, out safety)) errors.Add(Messages.NotANumber(ImportColumns.SafetyStockLabel, ss));
                else if (safety < 0) errors.Add(Messages.SafetyStockNegative);
            }

            string? hsCode = null, sstCode = null;
            if (tax.HsCode)
            {
                hsCode = TaxFieldShapes.Clean(r.Get(ImportColumns.HsCode));
                if (TaxFieldShapes.HsCodeError(hsCode) is { } hsError) errors.Add(hsError);
            }
            if (tax.SstCode)
            {
                sstCode = TaxFieldShapes.Clean(r.Get(ImportColumns.SstCode));
                if (TaxFieldShapes.SstCodeError(sstCode) is { } sstError) errors.Add(sstError);
            }

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

            var status = errors.Count > 0 ? ImportRowStatus.Error : existingSkus.Contains(sku) ? ImportRowStatus.Update : ImportRowStatus.New;
            results.Add(new ImportRowResult(r.Number, sku, errors.Count == 0, status, errors));
            if (errors.Count == 0) parsed.Add(new ProductRow(r.Number, sku, name, categoryId, unitId, safety, codes, hsCode, sstCode));
        }
        return (results, parsed);
    }

    private async Task<(int Created, int Updated)> CommitAsync(List<ProductRow> parsed, List<ImportRowResult> results, TaxColumns tax, CancellationToken ct)
    {
        // Validation found every category and unit, so the commit only looks them up; it never creates one.
        var cats = (await categories.ListAsync(ct)).ToDictionary(c => c.Id);
        var units = (await uoms.ListAsync(ct)).ToDictionary(u => u.Id);
        var existing = (await products.ListAsync(new ProductsBySkusSpec(parsed.Select(p => p.Sku).ToList()), ct)).ToDictionary(p => p.Sku.ToLowerInvariant());
        var withStock = (await balances.ListAsync(new ProductIdsWithBalanceSpec(existing.Values.Select(p => p.Id).ToList()), ct)).ToHashSet();
        int created = 0, updated = 0;
        var resultIndex = results.Select((r, i) => (r.Row, i)).ToDictionary(x => x.Row, x => x.i); // O(1) lookups below

        foreach (var row in parsed)
        {
            var cat = row.CategoryId is { } categoryId ? cats[categoryId] : null;
            var uom = units[row.UomId];

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
                        results[resultIndex[row.Row]] = new ImportRowResult(row.Row, row.Sku, true, ImportRowStatus.Update, [Messages.UnitNotChanged]);
                    else
                        p.ChangeBaseUnit(uom, hasStockMovements: false);
                }
                updated++;
            }
            if (tax.HsCode || tax.SstCode) p.SetTaxCodes(tax.HsCode ? row.HsCode : p.HsCode, tax.SstCode ? row.SstCode : p.SstCode);
            p.SetBarcodes(p.Barcodes.Select(b => b.Barcode).Concat(row.Barcodes));
        }

        audit.Record(AuditActions.ImportProducts, AuditEntities.Product, AuditEntities.Bulk, after: new { created, updated });
        await uow.SaveChangesAsync(ct);
        return (created, updated);
    }

    private static List<string> Split(string text) =>
        text.Split(ImportColumns.BarcodeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
}
