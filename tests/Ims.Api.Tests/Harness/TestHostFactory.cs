using Ims.Application.Features.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Harness;

/// <summary>
/// Runs the real application in-process in the "Testing" environment against the supplied database.
/// Program skips migrate/seed in that environment, so the fixture does it explicitly.
/// </summary>
public sealed class TestHostFactory(TestHostSettings settings) : WebApplicationFactory<Program>
{
    public const string TestingEnvironment = "Testing";

    /// <summary>The service registrations the host was built from; used to re-validate the container.</summary>
    public IServiceCollection? Registrations { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);

        // UseSetting reaches configuration before Program.cs reads it (the connection string is read while services are registered).
        builder.UseSetting("ConnectionStrings:Default", settings.Database.ConnectionString);
        builder.UseSetting("Seed:AdminPassword", settings.AdminPassword);
        builder.UseSetting("Seed:DemoPassword", settings.DemoPassword);
        builder.UseSetting("Jwt:Key", settings.JwtKey);
        // The per-user limit is not what these tests are about; the login limit is tested on its own address.
        builder.UseSetting("RateLimit:PerMinute", "1000000");
        builder.UseSetting("Logging:Directory", Path.Combine(Path.GetTempPath(), "ims-api-tests-logs"));
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        // The session tests below drive refresh and logout with the token from the (deprecated) response body. The shipped default is off;
        // RefreshCookieTests checks that default on a host which removes this override.
        builder.UseSetting("Auth:ReturnRefreshTokenInBody", "true");

        builder.ConfigureServices(services =>
        {
            Registrations = services;
            services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>();
        });

        // Production refuses to switch accounting on until PL1.8; most tests switch it on through the API to test what it does, so this host allows it.
        // ModuleNotYetSwitchableTests puts the production list back on a host of its own.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new NotYetSwitchableModules([]));
        });
    }
}
