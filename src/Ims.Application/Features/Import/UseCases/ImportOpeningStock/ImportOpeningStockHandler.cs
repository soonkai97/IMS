using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Import;

/// <summary>
/// Creates one draft receipt per warehouse from the file; the user reviews and posts it like any receipt (UX decision D7).
/// All-or-nothing (D6): the whole commit is one transaction, so a failure leaves nothing behind.
/// </summary>
public class ImportOpeningStockHandler(IExcelReader excel, IReadRepository<Product> products, IReadRepository<Warehouse> warehouses,
    IWarehouseScope scope, IDispatcher dispatcher, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ImportOpeningStockCommand, ImportResult>
{
    private record Parsed(int Row, int ProductId, int WarehouseId, decimal Qty, decimal Cost);

    public async Task<ImportResult> HandleAsync(ImportOpeningStockCommand cmd, CancellationToken ct)
    {
        var sheet = excel.Read(cmd.File, Limits.MaxImportRows);
        ImportSheet.RequireColumns(sheet, ImportColumns.Sku, ImportColumns.Warehouse, ImportColumns.Qty);

        var fileSkus = sheet.Rows.Select(r => r.Get(ImportColumns.Sku)).Where(s => s.Length > 0).Distinct().ToList();
        var skus = (await products.ListAsync(new ProductSkusBySkusSpec(fileSkus), ct)).Where(p => p.IsActive).ToDictionary(p => p.Sku.ToLowerInvariant(), p => p.Id);
        var whs = (await warehouses.ListAsync(new ActiveWarehousesSpec(), ct)).ToDictionary(w => w.Code.ToLowerInvariant(), w => w.Id);
        var allowed = scope.Ids();

        var results = new List<ImportRowResult>();
        var parsed = new List<Parsed>();
        foreach (var r in sheet.Rows)
        {
            var errors = new List<string>();
            var sku = r.Get(ImportColumns.Sku);
            var code = r.Get(ImportColumns.Warehouse);
            if (!skus.TryGetValue(sku.ToLowerInvariant(), out var pid)) errors.Add(Messages.UnknownSku(sku));
            if (!whs.TryGetValue(code.ToLowerInvariant(), out var wid)) errors.Add(Messages.UnknownWarehouse(code));
            else if (allowed is not null && !allowed.Contains(wid)) errors.Add(Messages.WarehouseNoAccess(code));
            var qtyText = r.GetNumber(ImportColumns.Qty);
            if (!ImportSheet.TryParseDecimal(qtyText, out var qty)) errors.Add(qtyText.Length == 0 ? Messages.QtyPositive : Messages.NotANumber(ImportColumns.QtyLabel, qtyText));
            else if (qty <= 0 || qty > Limits.MaxQuantity) errors.Add(Messages.QtyPositive);
            decimal cost = 0;
            if (r.GetNumber(ImportColumns.UnitCost) is { Length: > 0 } uc)
            {
                if (!ImportSheet.TryParseDecimal(uc, out cost)) errors.Add(Messages.NotANumber(ImportColumns.UnitCostLabel, uc));
                else if (cost < 0) errors.Add(Messages.CostNegative);
                else if (!Money.IsExact(cost)) errors.Add(Messages.CostTooPrecise);
            }

            // Every valid row becomes a line on a new draft receipt, so it is always "New".
            results.Add(new ImportRowResult(r.Number, sku, errors.Count == 0, errors.Count == 0 ? ImportRowStatus.New : ImportRowStatus.Error, errors));
            if (errors.Count == 0) parsed.Add(new Parsed(r.Number, pid, wid, qty, cost));
        }

        if (!cmd.Commit)
            return new ImportResult(false, results.Count, results.Count(r => r.Ok), results.Count(r => !r.Ok), parsed.Count, 0, null, results);

        ImportSheet.EnsureNoErrors(results);
        var firstDoc = await uow.ExecuteAsync(async () =>
        {
            int? first = null;
            foreach (var g in parsed.GroupBy(x => x.WarehouseId))
            {
                var request = new DocumentSaveRequest(g.Key, null, null, ImportColumns.OpeningStockRemark,
                    g.Select(x => new DocumentLineInput(x.ProductId, x.Qty, null, x.Cost, null)).ToList());
                var doc = await dispatcher.SendAsync(new CreateDocumentCommand(DocumentType.Receipt, request), ct);
                first ??= doc.Id;
            }
            audit.Record(AuditActions.ImportOpeningStock, AuditEntities.StockDocument, AuditEntities.Bulk, after: new { rows = parsed.Count });
            await uow.SaveChangesAsync(ct);
            return first;
        }, ct);

        return new ImportResult(true, results.Count, results.Count, 0, parsed.Count, 0, firstDoc, results);
    }
}
