using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Reports;

public class ExportMovementsHandler(IReadRepository<StockDocumentLine> lines, IWarehouseScope scope, IReportWriter report)
    : IQueryHandler<ExportMovementsQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(ExportMovementsQuery q, CancellationToken ct)
    {
        var rows = await lines.ListAsync(new MovementsSpec(q.Filter, scope.Ids()), ct);
        var bytes = report.Write(ReportSheets.Movements, ReportHeaders.MovementColumns, rows.Select(r => new object?[]
        {
            r.PostedAt.AddHours(Limits.MalaysiaUtcOffsetHours), r.DocNo, r.Type.ToString(), r.Status.ToString(), r.WarehouseCode, r.Partner,
            r.Sku, r.ProductName, r.Qty, r.UomCode, r.BaseQty, r.UnitCost, r.Amount
        }));
        return new ExportFile(bytes, ReportFiles.Movements, ContentTypes.Xlsx);
    }
}
