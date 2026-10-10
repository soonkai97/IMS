namespace Ims.Application;

public static class ReportHeaders
{
    public static readonly string[] StockSummary =
        ["SKU", "Product", "Category", "Warehouse", "Unit", "On hand", "In transit", "Avg cost", "Value", "Safety stock", "Low"];
    public static readonly string[] Movements =
        ["Posted (MYT)", "Document", "Type", "Status", "Warehouse", "Partner", "SKU", "Product", "Qty", "Unit", "Base qty", "Stock effect", "Unit cost", "Amount"];
    public static readonly string[] Ledger =
        ["Time (MYT)", "SKU", "Product", "Warehouse", "Qty change", "Unit cost", "Reason", "Document", "User"];
    public static readonly string[] LowStock =
        ["SKU", "Product", "Warehouse", "On hand", "Safety stock", "Shortfall"];
    public const string LowFlag = "LOW";
}
