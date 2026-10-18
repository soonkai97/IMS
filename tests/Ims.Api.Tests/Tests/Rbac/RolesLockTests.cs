using System.Diagnostics;
using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// The roles lock serialises the user save, the user create and the role permission replace. A holder that stays too long must end in a clean 409 at once, not in five more waits
/// and a 500 when the request times out; and the password hash (slow on purpose) is never computed inside the lock. The host waits 0.5 s for the lock instead of 5 s.
/// </summary>
[Collection(ApiCollection.Name)]
public class RolesLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int WaitMs = 500;

    private readonly List<bool> _hashedInATransaction = [];
    private WebApplicationFactory<Program> _host = null!;
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        _host = fixture.CreateHost(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<ITransactionLocker>(sp => new SqlTransactionLocker(sp.GetRequiredService<AppDbContext>(), rolesLockTimeoutMs: WaitMs));
            s.AddScoped<IPasswordHasher>(sp => new SpyHasher(sp.GetRequiredService<AppDbContext>(), _hashedInATransaction));
        }));
        _admin = ApiFixture.CreateClient(_host);
        _admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>Hashes like the real one but notes whether a transaction (so the roles lock) was open at the time.</summary>
    private sealed class SpyHasher(AppDbContext db, List<bool> seen) : IPasswordHasher
    {
        private readonly Ims.Infrastructure.Security.IdentityPasswordHasher _real = new();

        public string Hash(string password)
        {
            lock (seen) seen.Add(db.Database.CurrentTransaction is not null);
            return _real.Hash(password);
        }

        public bool Verify(string hash, string password) => _real.Verify(hash, password);
    }

    /// <summary>Takes the roles lock in a transaction of its own; disposing the connection rolls back and releases it.</summary>
    private static async Task<SqlConnection> HoldRolesLockAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand("EXEC sp_getapplock @Resource = N'roles', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0", connection, tx);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private async Task<(ApiResponse Answer, TimeSpan Took)> TimedAsync(Func<Task<ApiResponse>> send)
    {
        var clock = Stopwatch.StartNew();
        var answer = await send();
        return (answer, clock.Elapsed);
    }

    [Fact]
    public async Task A_busy_roles_lock_answers_409_at_once_for_the_user_save_the_user_create_and_the_permission_replace_and_works_when_free()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        var role = (await _admin.CreateAsync(Unique.Name("Busy"), Permissions.StockRead)).Data<RoleDto>();
        var save = new UserSaveRequest(account.Username, account.Email, "Renamed", Roles.Clerk, true, null, account.WarehouseIds.ToList());
        var newUser = new UserSaveRequest(Unique.Username("busy"), Unique.Email("busy"), "New", Roles.Clerk, true, $"Pw-{Guid.NewGuid():N}-1!", account.WarehouseIds.ToList());

        var holder = await HoldRolesLockAsync(fixture.Database.ConnectionString);
        try
        {
            var attempts = new (string Name, Func<Task<ApiResponse>> Send)[]
            {
                ("user save", () => _admin.Users.UpdateAsync(account.Id, save)),
                ("user create", () => _admin.Users.CreateAsync(newUser)),
                ("permission replace", () => _admin.ReplaceAsync(role, [Permissions.StockRead, Permissions.ReportsRead]))
            };
            foreach (var (name, send) in attempts)
            {
                var (answer, took) = await TimedAsync(send);
                Assert.True(took < TimeSpan.FromSeconds(2), $"{name} waited {took} (one wait is {WaitMs} ms; five retried waits would take over 2.5 s)");
                Assert.Equal(Messages.LockBusy, answer.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
            }
        }
        finally
        {
            await holder.DisposeAsync();
        }

        (await _admin.Users.UpdateAsync(account.Id, save)).ShouldBeOk();
        (await _admin.Users.CreateAsync(newUser)).ShouldBeOk();
        (await _admin.ReplaceAsync(role, [Permissions.StockRead, Permissions.ReportsRead])).ShouldBeOk();
    }

    [Fact]
    public async Task The_password_is_hashed_before_the_transaction_so_never_while_the_roles_lock_is_held()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        _hashedInATransaction.Clear();

        var save = new UserSaveRequest(account.Username, account.Email, account.Username, Roles.Clerk, true, $"Pw-{Guid.NewGuid():N}-2!", account.WarehouseIds.ToList());
        (await _admin.Users.UpdateAsync(account.Id, save)).ShouldBeOk();
        var name = Unique.Username("hash");
        (await _admin.Users.CreateAsync(new UserSaveRequest(name, Unique.Email(name), "New", Roles.Clerk, true, $"Pw-{Guid.NewGuid():N}-1!", account.WarehouseIds.ToList()))).ShouldBeOk();

        Assert.Equal([false, false], _hashedInATransaction);   // one hash per request, none inside a transaction
    }
}
