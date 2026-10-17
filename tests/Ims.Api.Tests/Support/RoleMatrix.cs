using Ims.Domain;

namespace Ims.Api.Tests.Support;

/// <summary>
/// What each role may do, written out independently of the production code (PRD section 3.1 as the team reads it, see docs/design/mvp-ux.md 1.1 and the
/// permission list of docs/architecture/adr-roles-permissions.md 2.1). The endpoint tests compare the running API against this table, so a change in the seeded
/// grants or in a policy that the PRD does not support shows up as a failing test. The Owner holds only the large approval tier.
/// </summary>
public static class RoleMatrix
{
    private static readonly Dictionary<string, HashSet<string>> Allowed = new()
    {
        [Roles.Admin] =
        [
            Permissions.UsersManage, Permissions.UsersRead, Permissions.RolesManage, Permissions.RolesRead, Permissions.MasterDataManage, Permissions.MasterDataRead,
            Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.DocumentEditAny, Permissions.DocumentApprove, Permissions.DocumentApproveLarge,
            Permissions.DocumentPost, Permissions.DocumentPostApproved, Permissions.DocumentReverse, Permissions.StockNegativeOverride, Permissions.StockRead,
            Permissions.ReportsRead, Permissions.AuditRead, Permissions.SettingsManage, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.Owner] =
        [
            Permissions.MasterDataRead, Permissions.DocumentApproveLarge,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.Supervisor] =
        [
            Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.DocumentEditAny,
            Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentPostApproved, Permissions.DocumentReverse, Permissions.StockNegativeOverride,
            Permissions.StockRead, Permissions.ReportsRead, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.WarehouseStaff] = [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.StockRead],
        [Roles.Clerk] = [Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.StockRead, Permissions.CostView]
    };

    /// <summary>Whether the role may use an endpoint guarded by <paramref name="policy"/>: a permission code, or a policy of <see cref="AuthPolicies"/> that any one of its codes opens.</summary>
    public static bool MayUse(string role, string policy) =>
        AuthPolicies.AnyOf.TryGetValue(policy, out var anyOf) ? anyOf.Any(Allowed[role].Contains) : Allowed[role].Contains(policy);
}
