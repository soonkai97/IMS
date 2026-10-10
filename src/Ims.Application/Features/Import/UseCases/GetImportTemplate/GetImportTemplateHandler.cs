using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Import;

public class GetImportTemplateHandler(IReportWriter report) : IQueryHandler<GetImportTemplateQuery, ExportFile>
{
    public Task<ExportFile> HandleAsync(GetImportTemplateQuery q, CancellationToken ct)
    {
        var result = q.Kind == ImportKinds.OpeningStock
            ? new ExportFile(report.Write(ImportTemplates.OpeningStockSheet, ImportTemplates.OpeningStockColumns,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleWarehouse, ImportTemplates.SampleQty, ImportTemplates.SampleCost]]),
                ImportTemplates.OpeningStockFile, ContentTypes.Xlsx)
            : new ExportFile(report.Write(ImportTemplates.ProductsSheet, ImportTemplates.ProductColumns,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleName, ImportTemplates.SampleCategory, ImportColumns.DefaultUnit,
                  ImportTemplates.SampleSafetyStock, ImportTemplates.SampleBarcodes]]),
                ImportTemplates.ProductsFile, ContentTypes.Xlsx);
        return Task.FromResult(result);
    }
}
