namespace Ims.Api.Tests.Harness;

/// <summary>Everything a test host needs that is created by the test run: database, JWT key and seed passwords. Nothing here is a real secret.</summary>
public sealed record TestHostSettings(TestDatabase Database, string JwtKey, string AdminPassword, string DemoPassword)
{
    public static TestHostSettings Generate(TestDatabase database) => new(
        database,
        JwtKey: Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
        AdminPassword: GeneratePassword("Adm"),
        DemoPassword: GeneratePassword("Demo"));

    private static string GeneratePassword(string prefix) => $"{prefix}-{Guid.NewGuid():N}-Aa1!";
}
