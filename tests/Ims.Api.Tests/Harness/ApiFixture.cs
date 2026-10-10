using Ims.Api.Tests.Clients;
using Ims.Application.Features.Auth;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Harness;

/// <summary>
/// Creates a fresh database, migrates and seeds it through the application's own code, starts the host, and drops the
/// database at the end. Tests get callers from <see cref="AsAsync"/> (cached logins) or <see cref="CreateClient"/>.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, (string Token, DateTime IssuedAt)> _sessions = [];
    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    public TestHostSettings Settings { get; private set; } = null!;
    public TestDatabase Database => Settings.Database;
    public TestHostFactory Host { get; private set; } = null!;
    public ReferenceData Reference { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Settings = TestHostSettings.Generate(TestDatabase.CreateNew());
        try
        {
            Host = new TestHostFactory(Settings);
            _ = Host.Services; // starts the host; ValidateOnBuild runs here

            using (var scope = Host.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            await Seeder.SeedAsync(Host.Services, development: true);

            Reference = await ReferenceData.LoadAsync(await AsAsync(Roles.Admin));
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Host is not null) await Host.DisposeAsync();
        if (Settings is not null) await Settings.Database.DropAsync();
    }

    /// <summary>A new caller with its own fake address; anonymous unless a token is given.</summary>
    public ApiClient CreateClient(string? accessToken = null)
    {
        var http = Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add(ClientAddressStartupFilter.HeaderName, ClientAddresses.Next());
        return new ApiClient(http, accessToken);
    }

    public ApiClient Anonymous() => CreateClient();

    /// <summary>A caller signed in as the seeded user of <paramref name="role"/>. The login is reused between tests.</summary>
    public async Task<ApiClient> AsAsync(string role)
    {
        await _sessionGate.WaitAsync();
        try
        {
            if (!_sessions.TryGetValue(role, out var session) || DateTime.UtcNow - session.IssuedAt > SessionLifetime)
            {
                var login = await CreateClient().Auth.LoginAsync(TestUsers.UsernameOf(role), PasswordOf(role));
                session = (login.Data<AuthResult>().AccessToken, DateTime.UtcNow);
                _sessions[role] = session;
            }
            return CreateClient(session.Token);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public string PasswordOf(string role) => role == Roles.Admin ? Settings.AdminPassword : Settings.DemoPassword;
}
