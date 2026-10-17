namespace Ims.Infrastructure;

public static class SeedDefaults
{
    public const string AdminUsername = "admin";
    public const string AdminEmail = "admin@ims.local";
    public const string AdminDisplayName = "Administrator";
    public const string DemoEmailDomain = "ims.local";
    public const string SupervisorUsername = "supervisor";
    public const string DefaultWarehouseCode = "WH01";
    public const string DefaultWarehouseName = "Main warehouse";
    public const string DefaultCategory = "General";
    public const string DefaultUnitCode = "PCS";
    public const string DemoSupplier = "Demo Supplier Sdn Bhd";
    public const string DemoCustomer = "Demo Customer";
    public const decimal DefaultApprovalAmount = 5000m;

    public static readonly (string Code, string Name)[] Units = [("PCS", "Piece"), ("CTN", "Carton"), ("KG", "Kilogram"), ("BOX", "Box")];
    /// <summary>Username, role code and whether the person sees every warehouse (the owner does; the others work in the default warehouse).</summary>
    public static readonly (string Username, string Role, bool AllWarehouses)[] DemoUsers =
    [
        ("owner", Ims.Domain.SystemRoleCodes.Owner, true), ("supervisor", Ims.Domain.SystemRoleCodes.Supervisor, false),
        ("staff", Ims.Domain.SystemRoleCodes.WarehouseStaff, false), ("clerk", Ims.Domain.SystemRoleCodes.Clerk, false)
    ];
    public static readonly (string Sku, string Name, decimal Safety, string Barcode)[] DemoProducts =
    [
        ("DEMO-001", "Demo widget", 20m, "9556000000011"),
        ("DEMO-002", "Demo bolt M8", 100m, "9556000000028")
    ];
}
