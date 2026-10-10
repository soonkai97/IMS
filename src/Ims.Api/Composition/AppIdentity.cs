namespace Ims.Api;

/// <summary>
/// The application's name, derived from the assembly (Ims.Api -> "Ims") so titles, log files and log properties follow the
/// project name automatically when this solution is used as a base and renamed.
/// </summary>
public static class AppIdentity
{
    private static readonly string AssemblyNameValue = typeof(AppIdentity).Assembly.GetName().Name ?? "App.Api";

    /// <summary>Full assembly name, e.g. "Ims.Api".</summary>
    public static string AssemblyName => AssemblyNameValue;

    /// <summary>Product name, e.g. "Ims".</summary>
    public static string Name => AssemblyNameValue.Split('.')[0];

    /// <summary>Lower-case product name for file names, e.g. "ims".</summary>
    public static string Slug => Name.ToLowerInvariant();

    public static string ApiTitle => $"{Name} API";
}
