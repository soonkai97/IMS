using Ims.Domain;

namespace Ims.Api.Tests.Harness;

/// <summary>The demo users created by the seeder, one per role. The admin has its own password; the others share the demo password.</summary>
public static class TestUsers
{
    public static readonly string[] AllRoles = Roles.All;

    public static string UsernameOf(string role) => role switch
    {
        Roles.Admin => "admin",
        Roles.Owner => "owner",
        Roles.Supervisor => "supervisor",
        Roles.WarehouseStaff => "staff",
        Roles.Clerk => "clerk",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };
}
