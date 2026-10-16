namespace Ims.Domain;

/// <summary>
/// Every permission the system knows: the source of truth for what can be granted. A code is stable (never renamed once released), listed once,
/// and named by a constant in <see cref="Permissions"/>. The order is the order of the role editor. See docs/architecture/adr-roles-permissions.md 2.1.
/// </summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        // ----- Administration -----
        new(Permissions.UsersRead, PermissionGroup.Administration, "See the list of users with their role and warehouses.")
            { AdminCore = true },
        new(Permissions.UsersManage, PermissionGroup.Administration, "Create and edit users, reset passwords and deactivate accounts.")
            { IsDangerous = true, AdminCore = true, Requires = [Permissions.UsersRead] },
        new(Permissions.RolesRead, PermissionGroup.Administration, "See the roles and what each of them may do.")
            { AdminCore = true },
        new(Permissions.RolesManage, PermissionGroup.Administration, "Create, change and delete roles and choose what each role may do.")
            { IsDangerous = true, AdminCore = true, Requires = [Permissions.RolesRead] },
        new(Permissions.SettingsManage, PermissionGroup.Administration, "Change the system settings and the approval rules.")
            { IsDangerous = true },
        new(Permissions.AuditRead, PermissionGroup.Administration, "Read the audit log.")
            { IsDangerous = true },

        // ----- Master data -----
        new(Permissions.MasterDataRead, PermissionGroup.MasterData, "See products, warehouses, partners and the other master data."),
        new(Permissions.MasterDataManage, PermissionGroup.MasterData, "Create, change and deactivate products, warehouses and the other master data.")
            { Requires = [Permissions.MasterDataRead] },
        new(Permissions.PartnersCreate, PermissionGroup.MasterData, "Add a supplier or a customer; changing one needs master data management."),

        // ----- Documents -----
        new(Permissions.DocumentCreate, PermissionGroup.Documents, "Create receipts and issues, edit them as drafts and submit them."),
        new(Permissions.DocumentEditAny, PermissionGroup.Documents, "Edit, submit or cancel the drafts and submissions of other people, not only your own.")
            { Requires = [Permissions.DocumentCreate] },
        new(Permissions.DocumentApprove, PermissionGroup.Documents, "Approve or reject the standard documents that need approval."),
        new(Permissions.DocumentApproveLarge, PermissionGroup.Documents, "Approve or reject the large documents that need the higher approval."),
        new(Permissions.DocumentPost, PermissionGroup.Documents, "Post a document so that it changes the stock."),
        new(Permissions.DocumentPostApproved, PermissionGroup.Documents, "Post a document that needed approval.")
            { Requires = [Permissions.DocumentPost] },
        new(Permissions.DocumentReverse, PermissionGroup.Documents, "Reverse a posted document, with a reason.")
            { IsDangerous = true },
        new(Permissions.StockNegativeOverride, PermissionGroup.Documents, "Post an issue even when it takes the stock below zero.")
            { IsDangerous = true, Requires = [Permissions.DocumentPost] },

        // ----- Stock and reports -----
        new(Permissions.StockRead, PermissionGroup.StockAndReports, "See stock levels, the ledger and the low-stock list."),
        new(Permissions.ReportsRead, PermissionGroup.StockAndReports, "Open and export the reports."),

        // ----- Cost and value -----
        new(Permissions.CostView, PermissionGroup.CostAndValue, "See unit cost, line cost and document totals on the documents you work with.")
            { IsDangerous = true },
        new(Permissions.ValuationView, PermissionGroup.CostAndValue, "See what the stock is worth: average cost, value and the stock total.")
            { IsDangerous = true, Requires = [Permissions.CostView] },
    ];

    /// <summary>The codes in catalog order.</summary>
    public static readonly IReadOnlyList<string> Codes = All.Select(p => p.Code).ToArray();

    // First entry wins on a duplicate code: the catalog tests report the duplicate instead of the type failing to initialise.
    private static readonly Dictionary<string, PermissionDefinition> ByCode = All.GroupBy(p => p.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);

    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);
}
