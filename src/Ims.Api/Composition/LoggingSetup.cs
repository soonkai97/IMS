using Serilog;
namespace Ims.Api;

public static class LoggingSetup
{
    /// <summary>Structured logs to console and a rolling daily file (30 days), every line tagged with the request's correlation id.</summary>
    public static IHostBuilder AddStructuredLogging(this IHostBuilder host) =>
        host.UseSerilog((ctx, services, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", LogTemplates.ApplicationName)
            .WriteTo.Console(outputTemplate: LogTemplates.Console)
            .WriteTo.File(
                path: Path.Combine(ctx.Configuration[ApiConfigKeys.LogDirectory] ?? ApiConfigKeys.DefaultLogDirectory, LogTemplates.File),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: LogTemplates.RetainedFiles,
                outputTemplate: LogTemplates.FileLine));
}
