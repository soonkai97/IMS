namespace Ims.Infrastructure;

/// <summary>Infrastructure-layer constants: configuration keys, SQL types, seed data. Shared values live in Ims.Domain/Common/Constants.cs.</summary>
public static class ConfigKeys
{
    public const string ConnectionString = "Default";
    public const string JwtSection = "Jwt";
    public const string AccessSection = "Access";
    public const string StorageSection = "Storage";
    public const string SeedAdminPassword = "Seed:AdminPassword";
    public const string SeedDemoPassword = "Seed:DemoPassword";
}
