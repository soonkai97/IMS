using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ims.Infrastructure.Storage;

/// <summary>Healthy when the storage folder exists (or can be created) and a file can be written and removed.</summary>
public sealed class StorageHealthCheck(IOptions<StorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var root = Path.GetFullPath(options.Value.RootPath);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [1], ct);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("File storage is not writable.", ex);
        }
    }
}
