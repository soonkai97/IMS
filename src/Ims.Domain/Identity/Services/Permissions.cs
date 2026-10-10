namespace Ims.Domain;

/// <summary>Feature permissions (PRD section 3.1). The API maps each endpoint to one of these.</summary>
public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string UsersRead = "users.read";
    public const string MasterDataManage = "masterdata.manage";
    public const string MasterDataRead = "masterdata.read";
    public const string DocumentCreate = "document.create";
    public const string DocumentApprove = "document.approve";
    public const string DocumentPost = "document.post";
    public const string DocumentReverse = "document.reverse";
    public const string StockRead = "stock.read";
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";
    public const string SettingsManage = "settings.manage";

    public static readonly string[] All =
    [
        UsersManage, UsersRead, MasterDataManage, MasterDataRead, DocumentCreate, DocumentApprove,
        DocumentPost, DocumentReverse, StockRead, ReportsRead, AuditRead, SettingsManage
    ];

    private static readonly Dictionary<string, string[]> ByRole = new()
    {
        [Roles.Admin] = All,
        [Roles.Owner] = [UsersRead, MasterDataRead, DocumentApprove, StockRead, ReportsRead, AuditRead],
        [Roles.Supervisor] =
        [
            UsersRead, MasterDataManage, MasterDataRead, DocumentCreate, DocumentApprove, DocumentPost,
            DocumentReverse, StockRead, ReportsRead
        ],
        [Roles.WarehouseStaff] = [MasterDataRead, DocumentCreate, DocumentPost, StockRead],
        [Roles.Clerk] = [MasterDataRead, DocumentCreate, StockRead],
    };

    public static IReadOnlyCollection<string> For(string role) =>
        ByRole.TryGetValue(role, out var p) ? p : [];

    public static bool Has(string role, string permission) => For(role).Contains(permission);
}
