using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Import;

public class GetImportTemplateHandler(IExcelWriter excel) : IQueryHandler<GetImportTemplateQuery, ExportFile>
{
    public Task<ExportFile> HandleAsync(GetImportTemplateQuery q, CancellationToken ct)
    {
        var result = q.Kind == ImportKinds.OpeningStock
            ? new ExportFile(excel.Write(ImportTemplates.OpeningStockSheet, ImportTemplates.OpeningStockHeaders,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleWarehouse, ImportTemplates.SampleQty, ImportTemplates.SampleCost]]),
                ImportTemplates.OpeningStockFile, ContentTypes.Xlsx)
            : new ExportFile(excel.Write(ImportTemplates.ProductsSheet, ImportTemplates.ProductHeaders,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleName, ImportTemplates.SampleCategory, ImportColumns.DefaultUnit,
                  ImportTemplates.SampleSafetyStock, ImportTemplates.SampleBarcodes]]),
                ImportTemplates.ProductsFile, ContentTypes.Xlsx);
        return Task.FromResult(result);
    }
}
