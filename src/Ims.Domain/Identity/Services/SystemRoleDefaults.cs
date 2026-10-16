namespace Ims.Domain;

/// <summary>
/// What the five system roles hold by default (PRD 3.1, docs/design/mvp-ux.md 1.1): the permission matrix as defaults, not as a rule. The one place
/// that still names roles for rights. A fresh installation seeds these, and until roles come from the database (RP-4a) <see cref="RightsFor"/> is
/// how the Domain policies learn what a signed-in role may do. The oracle fixtures prove they reproduce the behaviour of the role-keyed map.
/// </summary>
public static class SystemRoleDefaults
{
    private static readonly Dictionary<string, string[]> ByRole = new(StringComparer.Ordinal)
    {
        [Roles.Admin] = [.. PermissionCatalog.Codes],
        [Roles.Owner] =
        [
            Permissions.MasterDataRead, Permissions.DocumentApproveLarge, Permissions.StockRead, Permissions.ReportsRead, Permissions.AuditRead,
            Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.Supervisor] =
        [
            Permissions.MasterDataManage, Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.DocumentEditAny,
            Permissions.DocumentApprove, Permissions.DocumentPost, Permissions.DocumentPostApproved, Permissions.DocumentReverse,
            Permissions.StockNegativeOverride, Permissions.StockRead, Permissions.ReportsRead, Permissions.CostView, Permissions.ValuationView
        ],
        [Roles.WarehouseStaff] = [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.StockRead],
        [Roles.Clerk] = [Permissions.MasterDataRead, Permissions.PartnersCreate, Permissions.DocumentCreate, Permissions.StockRead, Permissions.CostView],
    };

    // Computed once: asking for the rights of a role is a dictionary lookup, however often a list asks.
    private static readonly Dictionary<string, EffectivePermissions> Effective =
        ByRole.ToDictionary(r => r.Key, r => EffectivePermissions.Compute(r.Value), StringComparer.Ordinal);

    /// <summary>The permissions a system role holds by default; none for a role the system does not know.</summary>
    public static IReadOnlyCollection<string> For(string roleCode) => ByRole.GetValueOrDefault(roleCode) ?? [];

    /// <summary>The rights of a signed-in person with this role (none for an unknown role).</summary>
    public static ActorRights RightsFor(string roleCode, int userId) =>
        new(userId, Effective.GetValueOrDefault(roleCode) ?? EffectivePermissions.None);
}
