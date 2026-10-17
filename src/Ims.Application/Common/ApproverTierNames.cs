using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The deprecated <c>roleRequired</c> text of an approval rule (ADR roles-permissions 2.7), kept for one release so a client that still sends or shows a role name keeps working.
/// The tier is the only record: Owner and Admin name the Large tier, the other three system roles the Standard tier (what "a rule above a supervisor" meant), and a tier
/// is shown as the system role that holds it by default. Nothing else in the application reads these names.
/// </summary>
public static class ApproverTierNames
{
    public static string RoleOf(ApproverTier tier) => tier == ApproverTier.Large ? SystemRoleCodes.Owner : SystemRoleCodes.Supervisor;

    /// <summary>The tier a request asks for, or null when it names none, names an unknown role, or the two disagree.</summary>
    public static ApproverTier? Resolve(string? roleRequired, ApproverTier? tier)
    {
        ApproverTier? fromRole = roleRequired switch
        {
            null => null,
            SystemRoleCodes.Owner or SystemRoleCodes.Admin => ApproverTier.Large,
            SystemRoleCodes.Supervisor or SystemRoleCodes.WarehouseStaff or SystemRoleCodes.Clerk => ApproverTier.Standard,
            _ => null
        };
        if (roleRequired is not null && fromRole is null) return null;
        if (tier is not null && fromRole is not null && tier != fromRole) return null;
        var resolved = tier ?? fromRole;
        return resolved is { } t && Enum.IsDefined(t) ? t : null;
    }
}
