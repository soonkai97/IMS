namespace Ims.Api;

/// <summary>Sub-route segments used in [Http*] attributes.</summary>
public static class RouteSegments
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string Logout = "logout";
    public const string Me = "me";
    public const string ChangePassword = "change-password";
    public const string Receipts = "receipts";
    public const string Issues = "issues";
    public const string Submit = ApiRoutes.PublicId + "/submit";
    public const string Approve = ApiRoutes.PublicId + "/approve";
    public const string Reject = ApiRoutes.PublicId + "/reject";
    public const string Recall = ApiRoutes.PublicId + "/recall";
    public const string Cancel = ApiRoutes.PublicId + "/cancel";
    public const string Close = ApiRoutes.PublicId + "/close";
    public const string Post = ApiRoutes.PublicId + "/post";
    public const string Reverse = ApiRoutes.PublicId + "/reverse";
    public const string Receipt = ApiRoutes.PublicId + "/receipt";
    public const string Suggestions = "suggestions";
    public const string Lookup = "lookup";
    public const string Counts = "counts";
    public const string Filters = "filters";
    public const string Ledger = "ledger";
    public const string LowStock = "low-stock";
    public const string Summary = "summary";
    public const string Movements = "movements";
    public const string StockSummaryExport = "stock-summary/export";
    public const string MovementsExport = "movements/export";
    public const string LedgerExport = "ledger/export";
    public const string LowStockExport = "low-stock/export";
    public const string Products = "products";
    public const string OpeningStock = "opening-stock";
    public const string Templates = "templates/{kind}";
    public const string ModuleCode = "{code}";
}
