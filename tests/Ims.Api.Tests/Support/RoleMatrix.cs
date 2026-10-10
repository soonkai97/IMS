using Ims.Domain;

namespace Ims.Api.Tests.Support;

/// <summary>
/// What each role may do, written out independently of the production code (PRD section 3.1 as the team reads it, see
/// docs/design/mvp-ux.md 1.1). The endpoint tests compare the running API against this table, so a change in
/// <c>Permissions.cs</c> that the PRD does not support shows up as a failing test.
/// </summary>
public static class RoleMatrix
{
    private static readonly Dictionary<string, HashSet<string>> Allowed = new()
    {
        [Roles.Admin] =
        [
            Permissions.UsersManage, Permissions.UsersRead, Permissions.MasterDataManage, Permissions.MasterDataRead,
            Permissions.DocumentCreate, Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentReverse,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead, Permissions.SettingsManage
        ],
        [Roles.Owner] =
        [
            Permissions.UsersRead, Permissions.MasterDataRead, Permissions.DocumentApprove,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead
        ],
        [Roles.Supervisor] =
        [
            Permissions.UsersRead, Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.DocumentCreate,
            Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentReverse, Permissions.StockRead, Permissions.ReportsRead
        ],
        [Roles.WarehouseStaff] = [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.StockRead],
        [Roles.Clerk] = [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.StockRead]
    };

    public static bool MayUse(string role, string permission) => Allowed[role].Contains(permission);
}
