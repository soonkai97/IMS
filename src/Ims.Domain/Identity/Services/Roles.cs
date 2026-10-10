namespace Ims.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Owner = "Owner";
    public const string Supervisor = "Supervisor";
    public const string WarehouseStaff = "WarehouseStaff";
    public const string Clerk = "Clerk";

    public static readonly string[] All = [Admin, Owner, Supervisor, WarehouseStaff, Clerk];

    /// <summary>Admin and Owner see every warehouse; the others only their assigned ones.</summary>
    public static bool SeesAllWarehouses(string role) => role is Admin or Owner;
}
