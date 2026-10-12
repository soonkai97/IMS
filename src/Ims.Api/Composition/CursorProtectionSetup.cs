using Ims.Application;
using Microsoft.AspNetCore.DataProtection;
namespace Ims.Api;

/// <summary>
/// The key ring that encrypts list cursors. It lives in a directory named by <see cref="ApiConfigKeys.DataProtectionKeysPath"/>, so it survives restarts and can be shared
/// by every instance (a container mounts a volume there). Outside Production a folder under the temp directory is used when nothing is set, so development and tests just work.
/// The keys are secrets: they are never written inside the repository.
/// </summary>
public static class CursorProtectionSetup
{
    private const string StartupProbeList = "startup";
    private const string KeysPathRequired = $"{ApiConfigKeys.DataProtectionKeysPath} must name a writable directory in Production: it holds the keys that encrypt list cursors and must be shared by every instance.";

    public static IServiceCollection AddCursorProtection(this IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        var path = cfg[ApiConfigKeys.DataProtectionKeysPath];
        if (string.IsNullOrWhiteSpace(path))
        {
            if (env.IsProduction()) throw new InvalidOperationException(KeysPathRequired);
            path = Path.Combine(Path.GetTempPath(), ApiConfigKeys.DefaultDataProtectionKeysFolder);
        }

        services.AddDataProtection()
            .SetApplicationName(ApiConfigKeys.DataProtectionApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(path));
        services.AddSingleton<ICursorProtector, DataProtectionCursorProtector>();
        return services;
    }

    /// <summary>Protects one value at start-up: a key directory that cannot be written stops the boot with a clear message instead of failing the first full page of a list.</summary>
    public static void VerifyCursorProtection(this IServiceProvider services)
    {
        try
        {
            var cursors = services.GetRequiredService<ICursorProtector>();
            if (!cursors.TryUnprotect(StartupProbeList, cursors.Protect(StartupProbeList, [1]), out _)) throw new InvalidOperationException("The cursor key ring did not read back what it wrote.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The key ring for list cursors cannot be used. Check that {ApiConfigKeys.DataProtectionKeysPath} is a writable directory.", ex);
        }
    }
}
