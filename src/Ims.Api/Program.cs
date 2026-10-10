using Ims.Api;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure;
using Serilog;

// Bootstrap logger so startup failures are captured before the host is built.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.AddStructuredLogging();
    builder.ConfigureServerLimits();

    // Fail fast on a mis-wired container: every registration must resolve and no scoped service may leak into a singleton.
    builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });

    // Composition root. Each layer registers itself; this file only lists the layers.
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddPresentation(builder.Configuration, builder.Environment);

    var app = builder.Build().UsePresentation();

    // Migrate and seed on start-up in Development only by default; elsewhere it is opt-in (it races when several instances start at once).
    var isDevelopment = app.Environment.IsDevelopment();
    if (!app.Environment.IsEnvironment(EnvironmentNames.Testing) && app.Configuration.GetValue(ApiConfigKeys.MigrateOnStartup, isDevelopment))
        await app.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync(seedDemoData: isDevelopment);

    Log.Information("{Application} starting in {Environment}", AppIdentity.AssemblyName, app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "{Application} terminated unexpectedly", AppIdentity.AssemblyName);
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
