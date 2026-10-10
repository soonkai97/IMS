namespace Ims.Application;

public static class ImportTemplates
{
    public static readonly string[] ProductHeaders = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];
    public static readonly string[] OpeningStockHeaders = ["SKU", "Warehouse", "Qty", "UnitCost"];
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
