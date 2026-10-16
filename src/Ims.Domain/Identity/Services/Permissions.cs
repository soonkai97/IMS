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

    // ----- Added by the roles-and-permissions work (ADR roles-permissions). The role-keyed map below does not grant them and no endpoint policy asks for them
    // yet: SystemRoleDefaults grants them and the Domain policies read them (RP-1); the endpoints follow in RP-4a (document.*, stock.*) and RP-5 (roles.*). -----
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    /// <summary>Edit, submit or cancel any user's draft or submission in the caller's warehouses, not only one's own.</summary>
    public const string DocumentEditAny = "document.edit.any";
    /// <summary>Approve or reject a large document (<see cref="DocumentApprove"/> covers the standard ones).</summary>
    public const string DocumentApproveLarge = "document.approve.large";
    /// <summary>Post a document that needed approval. Requires <see cref="DocumentPost"/>.</summary>
    public const string DocumentPostApproved = "document.post.approved";
    /// <summary>BR-03: post an issue that takes the stock below zero. Requires <see cref="DocumentPost"/>.</summary>
    public const string StockNegativeOverride = "stock.negative.override";

    /// <summary>The 15 permissions the role-keyed map below grants and the endpoints enforce today (a subset of <see cref="PermissionCatalog"/>; removed with the map in RP-4a).</summary>
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
