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
    public const string SampleBarcodes = "9556000000011,9556000000028";
    public const decimal SampleSafetyStock = 10m;
    public const decimal SampleQty = 100m;
    public const decimal SampleCost = 12.5m;
}
