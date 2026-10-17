namespace Ims.Domain;

/// <summary>
/// The codes of the five system roles the installation seeds (ADR roles-permissions 2.2). A role is data: nothing decides a right, a scope or an approval by
/// comparing one of these (the token claims do), they only name the seeded rows, the first administrator and the rows the tests sign in as.
/// </summary>
public static class SystemRoleCodes
{
    public const string Admin = "Admin";
    public const string Owner = "Owner";
    public const string Supervisor = "Supervisor";
    public const string WarehouseStaff = "WarehouseStaff";
    public const string Clerk = "Clerk";

    public static readonly string[] All = [Admin, Owner, Supervisor, WarehouseStaff, Clerk];
}
