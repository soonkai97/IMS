namespace Ims.Domain;

/// <summary>Feature permissions (PRD section 3.1). The API maps each endpoint to one of these.</summary>
public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string UsersRead = "users.read";
    public const string MasterDataManage = "masterdata.manage";
    public const string MasterDataRead = "masterdata.read";
    /// <summary>Create a supplier or customer. Editing and deactivating partners stays under <see cref="MasterDataManage"/>.</summary>
    public const string PartnersCreate = "partners.create";
    public const string DocumentCreate = "document.create";
    public const string DocumentApprove = "document.approve";
    public const string DocumentPost = "document.post";
    public const string DocumentReverse = "document.reverse";
    public const string StockRead = "stock.read";
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";
    public const string SettingsManage = "settings.manage";

    /// <summary>
    /// See the cost of the documents one works on: unit cost, line cost, document totals and estimates, and the approval amounts and thresholds.
    /// Data a role needs to do its job is shown by this permission; data tagged with <see cref="VisibleWithAttribute"/> is withheld without it.
    /// </summary>
    public const string CostView = "cost.view";

    /// <summary>See what the stock is worth: average cost and value of stock, the ledger's cost, the stock total. Never the Warehouse staff or Clerk.</summary>
    public const string ValuationView = "valuation.view";

    public static readonly string[] All =
    [
        UsersManage, UsersRead, MasterDataManage, MasterDataRead, PartnersCreate, DocumentCreate, DocumentApprove,
        DocumentPost, DocumentReverse, StockRead, ReportsRead, AuditRead, SettingsManage, CostView, ValuationView
    ];

    private static readonly Dictionary<string, string[]> ByRole = new()
    {
        [Roles.Admin] = All,
        [Roles.Owner] = [MasterDataRead, DocumentApprove, StockRead, ReportsRead, AuditRead, CostView, ValuationView],
        [Roles.Supervisor] =
        [
            MasterDataManage, MasterDataRead, PartnersCreate, DocumentCreate, DocumentApprove, DocumentPost,
            DocumentReverse, StockRead, ReportsRead, CostView, ValuationView
        ],
        [Roles.WarehouseStaff] = [MasterDataRead, DocumentCreate, DocumentPost, StockRead],
        [Roles.Clerk] = [MasterDataRead, PartnersCreate, DocumentCreate, StockRead, CostView],
    };

    public static IReadOnlyCollection<string> For(string role) =>
        ByRole.TryGetValue(role, out var p) ? p : [];

    public static bool Has(string role, string permission) => For(role).Contains(permission);
}
