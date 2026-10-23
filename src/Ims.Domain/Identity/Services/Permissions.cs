namespace Ims.Domain;

/// <summary>The names of the permission codes (PRD section 3.1); <see cref="PermissionCatalog"/> lists them. The API asks for one of them, or one of a few, at every endpoint.</summary>
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
    /// <summary>Turn a module on or off for the whole installation, with a reason (ADR modules-and-tracking-dimensions 4.6). Core, dangerous, held by the Administrator.</summary>
    public const string ModulesManage = "modules.manage";
    /// <summary>See purchase orders. Belongs to the <see cref="ModuleCodes.Purchasing"/> module; requires <see cref="MasterDataRead"/> (the supplier and product lookups).</summary>
    public const string PoRead = "po.read";
    /// <summary>Create, edit a draft, submit, recall, cancel and close purchase orders. Belongs to the <see cref="ModuleCodes.Purchasing"/> module; requires <see cref="PoRead"/>, <see cref="CostView"/> (an order carries prices) and <see cref="StockRead"/> (the reorder list shows stock).</summary>
    public const string PoManage = "po.manage";
    /// <summary>See sales orders. Belongs to the <see cref="ModuleCodes.Sales"/> module; requires <see cref="MasterDataRead"/> (the customer and product lookups).</summary>
    public const string SoRead = "so.read";
    /// <summary>Create, edit a draft, submit, cancel and close sales orders. Belongs to the <see cref="ModuleCodes.Sales"/> module; requires <see cref="SoRead"/> only: a selling price is not cost, so it does not need <see cref="CostView"/>.</summary>
    public const string SoManage = "so.manage";

    /// <summary>
    /// See the cost of the documents one works on: unit cost, line cost, document totals and estimates, and the approval amounts and thresholds.
    /// Data a role needs to do its job is shown by this permission; data tagged with <see cref="VisibleWithAttribute"/> is withheld without it.
    /// </summary>
    public const string CostView = "cost.view";

    /// <summary>See what the stock is worth: average cost and value of stock, the ledger's cost, the stock total. Never the Warehouse staff or Clerk.</summary>
    public const string ValuationView = "valuation.view";

    // ----- Added by the roles-and-permissions work (ADR roles-permissions). roles.* is asked for by the roles endpoints of RP-5. -----
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
}
