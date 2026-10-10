using Ardalis.Specification;
using Ims.Application.Features.Stock;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportLedgerHandler(IReadRepository<StockLedgerEntry> ledger, IWarehouseScope scope, IReportWriter report)
    : IQueryHandler<ExportLedgerQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportLedgerQuery q, CancellationToken ct)
    {
        var rows = await ledger.ListAsync(new LedgerRowsSpec(q.Filter, scope.Ids(), Limits.MaxExportRows), ct);
        var bytes = report.Write(ReportSheets.Ledger, ReportHeaders.LedgerColumns, rows.Select(r => new object?[]
        {
            r.OccurredAt.AddHours(Limits.MalaysiaUtcOffsetHours), r.Sku, r.ProductName, r.WarehouseCode, r.QtyDelta, r.UnitCost,
            r.Reason.ToString(), r.SourceDocNo, r.User
        }));
        return new ExportFile(bytes, ReportFiles.Ledger, ContentTypes.Xlsx);
    }
}
