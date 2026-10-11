using Ims.Domain;
namespace Ims.Application;

public static class ReportHeaders
{
    public static readonly ReportColumn[] StockSummaryColumns =
    [
        new("SKU"), new("Product"), new("Category"), new("Warehouse"), new("Unit"), new("On hand"), new("In transit"),
        new("Avg cost", Permissions.ValuationView), new("Value", Permissions.ValuationView), new("Safety stock"), new("Low")
    ];
    public static readonly ReportColumn[] MovementColumns =
    [
        new("Posted (MYT)"), new("Document"), new("Type"), new("Status"), new("Reversal"), new("Warehouse"), new("Partner"), new("SKU"), new("Product"), new("Qty"),
        new("Unit"), new("Base qty"), new("Stock effect"), new("Unit cost", Permissions.CostView), new("Amount", Permissions.CostView)
    ];
    public static readonly ReportColumn[] LedgerColumns =
    [
        new("Time (MYT)"), new("SKU"), new("Product"), new("Warehouse"), new("Qty change"), new("Unit cost", Permissions.ValuationView),
        new("Reason"), new("Document"), new("User")
    ];
    public static readonly ReportColumn[] LowStockColumns =
        [new("SKU"), new("Product"), new("Warehouse"), new("On hand"), new("Safety stock"), new("Shortfall")];

    /// <summary>The full header rows, for a caller who may see every column.</summary>
    public static readonly string[] StockSummary = Names(StockSummaryColumns);
    public static readonly string[] Movements = Names(MovementColumns);
    public static readonly string[] Ledger = Names(LedgerColumns);
    public static readonly string[] LowStock = Names(LowStockColumns);
    public const string LowFlag = "LOW";
    /// <summary>In the "Reversal" column of the movements export: the row is the reversal of the posting shown in the row for the same document and line (blank on a posting row).</summary>
    public const string ReversalFlag = "Yes";

    public static string[] Names(IEnumerable<ReportColumn> columns) => columns.Select(c => c.Header).ToArray();
}
