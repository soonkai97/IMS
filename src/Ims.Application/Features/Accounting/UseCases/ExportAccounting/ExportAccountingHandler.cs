using Ims.Application.Features.Reports;
using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>
/// The accounting file for a period: one row per posted document line, a reversal as the opposite row (see <see cref="AccountingLayout"/>), at cost. It carries cost, so the caller must hold
/// <c>cost.view</c> (and the master data and report permissions the file also shows) here as well as the policy that let the request in (the permission closure makes the two agree; this is the check that does not depend on it). It never cuts a file
/// short: above <see cref="Limits.MaxExportRows"/> rows the answer is a 400 that says to narrow the period. A warehouse the caller may not read is a 404, and the rows of such warehouses never
/// appear. Every file produced is audited (who, when, the filters and the number of rows; never the content), in the same save that precedes the answer, so no file leaves without its record.
/// </summary>
public class ExportAccountingHandler(IAccountingReader accounting, IWarehouseScope scope, IIdMap<Warehouse> warehouseIds, IReportWriter report, ICallerPermissions caller,
    IAuditTrail audit, IUnitOfWork uow) : IQueryHandler<ExportAccountingQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportAccountingQuery q, CancellationToken ct)
    {
        // Everything the permission requires (cost, master data, reports), asked again here: the closure of the role editor makes the policy imply them, this does not depend on it.
        if (PermissionCatalog.Find(Permissions.AccountingExport)!.Requires.Any(required => !caller.Has(required))) throw new ForbiddenException(Messages.AccountingExportNeedsPermissions);

        var from = q.Filter.From!.Value; // the validator refused a request without the dates
        var to = q.Filter.To!.Value;
        var readable = scope.Ids();
        var warehouseId = await warehouseIds.ToInternalOrNoRowAsync(q.Filter.WarehouseId, ct);
        if (warehouseId is { } w && (w == IdMapExtensions.NoRow || (readable is not null && !readable.Contains(w)))) throw new NotFoundException(Messages.WarehouseNotFound);

        // One row more than a file may hold: getting it means the period is too big, and no file is written.
        var rows = await accounting.ListAsync(from, to, warehouseId, readable, Limits.MaxExportRows + 1, ct);
        if (rows.Count > Limits.MaxExportRows) throw new ValidationException(Messages.AccountingExportTooLarge(Limits.MaxExportRows));

        var fileName = AccountingLayout.FileName(from, to);
        var bytes = report.Write(AccountingLayout.Sheet, AccountingLayout.Columns, rows.Select(Row));
        audit.Record(AuditActions.AccountingExported, AuditEntities.AccountingExport, fileName,
            after: new { From = from, To = to, Warehouse = q.Filter.WarehouseId, LimitedToWarehouses = readable is not null, Rows = rows.Count });
        await uow.SaveChangesAsync(ct);
        return new ExportFile(bytes, fileName, ContentTypes.Xlsx);
    }

    /// <summary>One cell per column of <see cref="AccountingLayout.Columns"/>. Text a person typed goes through <see cref="SpreadsheetText"/>; the date is the Kuala Lumpur day.</summary>
    private static object?[] Row(AccountingEntry e)
    {
        var direction = StockDirection.Of(e.Type) * (e.IsReversal ? -1 : 1);
        return
        [
            DateOnly.FromDateTime(e.At.AddHours(Limits.MalaysiaUtcOffsetHours)), SpreadsheetText.Safe(e.DocNo), e.Type.ToString(), e.Purpose?.ToString(),
            e.IsReversal ? SpreadsheetText.Safe(e.DocNo) : null, SpreadsheetText.Safe(e.WarehouseCode), SpreadsheetText.Safe(e.Partner), SpreadsheetText.Safe(e.PartnerTin),
            SpreadsheetText.Safe(e.PartnerSstRegNo), SpreadsheetText.Safe(e.Sku), SpreadsheetText.Safe(e.ProductName), SpreadsheetText.Safe(e.HsCode), SpreadsheetText.Safe(e.SstCode),
            SpreadsheetText.Safe(e.BaseUnit), Signed(e.BaseQty, direction), e.UnitCost, Signed(e.Amount, direction)
        ];
    }

    /// <summary>The value with the sign of the stock effect; a zero stays an unsigned zero.</summary>
    private static decimal Signed(decimal value, int direction) => value == 0m ? 0m : direction * value;
}
