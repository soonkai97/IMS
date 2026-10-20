namespace Ims.Application;

/// <summary>Header keys as the importer sees them (lower case, no spaces or underscores).</summary>
public static class ImportColumns
{
    public const string Sku = "sku";
    public const string Name = "name";
    public const string Category = "category";
    public const string Unit = "unit";
    public const string SafetyStock = "safetystock";
    public const string Barcodes = "barcodes";
    public const string Warehouse = "warehouse";
    public const string Qty = "qty";
    public const string UnitCost = "unitcost";
    /// <summary>Optional product columns of the accounting module: read only while it is on, refused in a file while it is off.</summary>
    public const string HsCode = "hscode";
    public const string SstCode = "sstcode";

    public const string DefaultUnit = "PCS";
    /// <summary>Column names as shown in a row error ("Safety stock 'abc' is not a number.").</summary>
    public const string HsCodeLabel = "HS code";
    public const string SstCodeLabel = "SST code";
    public const string SafetyStockLabel = "Safety stock";
    public const string QtyLabel = "Quantity";
    public const string UnitCostLabel = "Unit cost";
    public const string OpeningStockRemark = "Opening stock (import)";
    public static readonly char[] BarcodeSeparators = [',', ';'];
}
