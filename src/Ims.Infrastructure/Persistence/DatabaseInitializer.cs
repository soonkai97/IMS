using Ims.Application;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ims.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IServiceProvider services, IConfiguration cfg, ILogger<DatabaseInitializer> log) : IDatabaseInitializer
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1", ".", "(local)"];

    public Task InitializeAsync(bool seedDemoData, CancellationToken ct = default)
    {
        // Demo users with well-known passwords are only ever created in a database on this machine, never on a shared server.
        if (seedDemoData && !IsLocalDatabase())
        {
            log.LogWarning("Demo data was requested but the database is not local; it will not be seeded.");
            seedDemoData = false;
        }
        return Seeder.SeedAsync(services, seedDemoData, ct);
    }

    private bool IsLocalDatabase()
    {
        var source = new SqlConnectionStringBuilder(cfg.GetConnectionString(ConfigKeys.ConnectionString)).DataSource.Trim();
        var host = source.Split(',', '\\')[0].Trim();
        return host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) || LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    }
}
