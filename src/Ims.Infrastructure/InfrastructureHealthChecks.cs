using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Infrastructure;

public static class InfrastructureHealthChecks
{
    public const string DatabaseCheckName = "database";
    public const string StorageCheckName = "storage";

    /// <summary>Adds the checks for everything Infrastructure talks to (the database). The API stays unaware of the provider.</summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(this IHealthChecksBuilder builder) =>
        builder.AddDbContextCheck<AppDbContext>(DatabaseCheckName).AddCheck<StorageHealthCheck>(StorageCheckName);
}
