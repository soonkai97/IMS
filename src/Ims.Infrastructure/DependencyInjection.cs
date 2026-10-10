using Ims.Application;
using Ims.Application.Features.Attachments;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Ims.Infrastructure.Excel;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Security;
using Ims.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ims.Infrastructure;

/// <summary>
/// Composition of the Infrastructure layer: the adapters behind the Application's ports.
/// Lifetimes: stateless services are singletons; anything touching the DbContext is scoped (one per request).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddPersistence(cfg)
                .AddSecurity(cfg)
                .AddFileFormats()
                .AddFileStorage(cfg);
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration cfg)
    {
        var connection = cfg.GetConnectionString(ConfigKeys.ConnectionString)
                         ?? throw new InvalidOperationException($"ConnectionStrings:{ConfigKeys.ConnectionString} is not configured.");
        services.AddDbContext<AppDbContext>(o => o
            .UseSqlServer(connection, sql => sql
                .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                .CommandTimeout(Ims.Domain.Limits.DbCommandTimeoutSeconds)
                // Loading two or more collections in one query multiplies the rows (a cartesian product). Each collection gets
                // its own query instead; nothing is multiplied and the cost stays linear in the data actually returned.
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            // If someone forces a single query with two or more collection includes, fail loudly instead of silently exploding.
            .ConfigureWarnings(w => w
                .Throw(RelationalEventId.MultipleCollectionIncludeWarning)
                // Only child rows and attachments are ever soft-deleted; parents (products, warehouses) are deactivated, never deleted.
                .Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        // Open generics: one registration serves every aggregate. Both interfaces resolve to the same scoped instance type.
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStockSummaryReader, EfStockSummaryReader>();
        services.AddScoped<ILowStockReader, EfLowStockReader>();
        services.AddScoped<ITransactionLocker, SqlTransactionLocker>();
        services.AddSingleton<IExceptionMapper, SqlExceptionMapper>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        return services;
    }

    private static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddOptions<JwtOptions>()
            .Bind(cfg.GetSection(ConfigKeys.JwtSection))
            .PostConfigure<IHostEnvironment>(EnsureDevelopmentKey)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ITokenValidationSettings, JwtTokenValidationSettings>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        return services;
    }

    /// <summary>
    /// Physical file storage. In Development and tests the folder defaults to "storage" under the content root; everywhere else
    /// Storage:RootPath must be configured (and mounted on durable storage), otherwise the application refuses to start.
    /// </summary>
    private static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddOptions<StorageOptions>()
            .Bind(cfg.GetSection(ConfigKeys.StorageSection))
            .PostConfigure<IHostEnvironment>((o, env) =>
            {
                if (string.IsNullOrWhiteSpace(o.RootPath) && (env.IsDevelopment() || env.IsEnvironment(EnvironmentNames.Testing)))
                    o.RootPath = Path.Combine(env.ContentRootPath, "storage");
                else if (!string.IsNullOrWhiteSpace(o.RootPath) && !Path.IsPathRooted(o.RootPath))
                    o.RootPath = Path.Combine(env.ContentRootPath, o.RootPath);
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        // Scanning hook for uploads. The default accepts everything; register a real IUploadScanner here to turn scanning on.
        services.AddSingleton<IUploadScanner, NoOpUploadScanner>();
        return services;
    }

    private static IServiceCollection AddFileFormats(this IServiceCollection services)
    {
        services.AddSingleton<IExcelWriter, ClosedXmlExcelWriter>();
        services.AddSingleton<IExcelReader, ClosedXmlExcelReader>();
        return services;
    }

    /// <summary>Production must configure a real key. Development and tests get an ephemeral random one (tokens die on restart).</summary>
    private static void EnsureDevelopmentKey(JwtOptions options, IHostEnvironment env)
    {
        if (options.Key.Length >= Ims.Domain.Limits.JwtKeyMinLength) return;
        if (!env.IsDevelopment() && !env.IsEnvironment(EnvironmentNames.Testing)) return; // validation will fail the start
        options.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(Ims.Domain.Limits.JwtKeyGeneratedBytes));
    }
}
