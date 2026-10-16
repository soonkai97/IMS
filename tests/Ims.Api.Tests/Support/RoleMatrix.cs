using Ims.Api;
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
            Permissions.UsersManage, Permissions.UsersRead, Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.PartnersCreate,
            Permissions.DocumentCreate, Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentReverse,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead, Permissions.SettingsManage, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.Owner] =
        [
            Permissions.MasterDataRead, Permissions.DocumentApprove,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.Supervisor] =
        [
            Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate,
            Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentReverse, Permissions.StockRead, Permissions.ReportsRead,
            Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.WarehouseStaff] = [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.StockRead],
        [Roles.Clerk] = [Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.StockRead, Permissions.CostView]
    };

    /// <summary>
    /// A door is a permission, except <see cref="AuthPolicies.DocumentCancel"/>: the create right (Draft and Submitted) or the approve right
    /// (Approved), written out here again so that a change to the policy has to be a change to this table too.
    /// </summary>
    public static bool MayUse(string role, string permission) => permission == AuthPolicies.DocumentCancel
        ? Allowed[role].Contains(Permissions.DocumentCreate) || Allowed[role].Contains(Permissions.DocumentApprove)
        : Allowed[role].Contains(permission);
}
