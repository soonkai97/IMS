using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Import;

public class GetImportTemplateHandler(IReportWriter report) : IQueryHandler<GetImportTemplateQuery, ExportFile>
{
    public Task<ExportFile> HandleAsync(GetImportTemplateQuery q, CancellationToken ct)
    {
        if (q.Kind == ImportKinds.OpeningStock)
            return Task.FromResult(new ExportFile(report.Write(ImportTemplates.OpeningStockSheet, ImportTemplates.OpeningStockColumns,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleWarehouse, ImportTemplates.SampleQty, ImportTemplates.SampleCost]]),
                ImportTemplates.OpeningStockFile, ContentTypes.Xlsx));

        object?[] sample = [ImportTemplates.SampleSku, ImportTemplates.SampleName, ImportTemplates.SampleCategory, ImportColumns.DefaultUnit,
            ImportTemplates.SampleSafetyStock, ImportTemplates.SampleBarcodes];
        return Task.FromResult(new ExportFile(report.Write(ImportTemplates.ProductsSheet, ImportTemplates.ProductColumns, [sample]), ImportTemplates.ProductsFile, ContentTypes.Xlsx));
    }
}
