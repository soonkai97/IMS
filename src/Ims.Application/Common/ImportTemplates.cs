using Ims.Domain;
namespace Ims.Application;

public static class ImportTemplates
{
    public static readonly ReportColumn[] ProductColumns =
        [new("SKU"), new("Name"), new("Category"), new("Unit"), new("SafetyStock"), new("Barcodes")];
    /// <summary>Added to the products template while the accounting module is on; the sample row leaves them blank so the unchanged template stores no tax code.</summary>
    public static readonly ReportColumn[] TaxColumns = [new("HsCode"), new("SstCode")];
    /// <summary>The cost column is only in the template of a caller who may see cost (a file without the column imports at zero cost, as before).</summary>
    public static readonly ReportColumn[] OpeningStockColumns = [new("SKU"), new("Warehouse"), new("Qty"), new("UnitCost", Permissions.CostView)];
    public const string ProductsSheet = "Products";
    public const string OpeningStockSheet = "Opening stock";
    public const string ProductsFile = "products-template.xlsx";
    public const string OpeningStockFile = "opening-stock-template.xlsx";
    public const string SampleSku = "SKU-001";
    public const string SampleName = "Sample item";
    public const string SampleCategory = "General";
    public const string SampleWarehouse = "WH01";
    /// <summary>Obviously fake and outside the demo data, so the unchanged template can be uploaded without a barcode clash.</summary>
    public const string SampleBarcodes = "SAMPLE-0000000001,SAMPLE-0000000002";
    public const decimal SampleSafetyStock = 10m;
    public const decimal SampleQty = 100m;
    public const decimal SampleCost = 12.5m;
}
