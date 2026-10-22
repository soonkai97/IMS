using Ims.Application.Features.Modules;
using Ims.Application.Features.Reports;
using Ims.Domain;
namespace Ims.Application.Features.Import;

public class GetImportTemplateHandler(IReportWriter report, IModuleSwitches modules) : IQueryHandler<GetImportTemplateQuery, ExportFile>
{
    public async Task<ExportFile> HandleAsync(GetImportTemplateQuery q, CancellationToken ct)
    {
        if (q.Kind == ImportKinds.OpeningStock)
            return new ExportFile(report.Write(ImportTemplates.OpeningStockSheet, ImportTemplates.OpeningStockColumns,
                [[ImportTemplates.SampleSku, ImportTemplates.SampleWarehouse, ImportTemplates.SampleQty, ImportTemplates.SampleCost]]),
                ImportTemplates.OpeningStockFile, ContentTypes.Xlsx);

        // The tax columns are part of the products template only while the accounting module is on (a file that carries them while it is off is refused).
        var accounting = await modules.IsEnabledAsync(TaxFieldNames.RemovedModule, ct);
        ReportColumn[] columns = accounting ? [.. ImportTemplates.ProductColumns, .. ImportTemplates.TaxColumns] : ImportTemplates.ProductColumns;
        object?[] sample = [ImportTemplates.SampleSku, ImportTemplates.SampleName, ImportTemplates.SampleCategory, ImportColumns.DefaultUnit,
            ImportTemplates.SampleSafetyStock, ImportTemplates.SampleBarcodes];
        if (accounting) sample = [.. sample, null, null];
        return new ExportFile(report.Write(ImportTemplates.ProductsSheet, columns, [sample]), ImportTemplates.ProductsFile, ContentTypes.Xlsx);
    }
}
