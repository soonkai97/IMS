using System.Diagnostics;
using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL1.1 acceptance 6: a flip takes the <c>modules</c> lock. A holder that stays too long ends a flip in a clean 409 at once (the host waits 0.5 s instead of 5 s), with nothing
/// written.
/// </summary>
[Collection(ApiCollection.Name)]
public class ModulesLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int WaitMs = 500;

    private WebApplicationFactory<Program> _host = null!;
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _host = fixture.CreateHost(b => b.ConfigureTestServices(s =>
            s.AddScoped<ITransactionLocker>(sp => new SqlTransactionLocker(sp.GetRequiredService<AppDbContext>(), modulesLockTimeoutMs: WaitMs))));
        _admin = ApiFixture.CreateClient(_host);
        _admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await ResetAsync();
    }

    private Task ResetAsync() => Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture); // also closes any order another test left open: the purchasing check refuses to switch off over one

    /// <summary>Takes an application lock in a transaction of its own; disposing the connection rolls back and releases it.</summary>
    private async Task<SqlConnection> HoldLockAsync(string resource)
    {
        var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = new SqlCommand($"EXEC sp_getapplock @Resource = N'{resource}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0", connection, tx);
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    [Fact]
    public async Task A_busy_modules_lock_answers_409_at_once_writes_nothing_and_the_flip_works_when_the_lock_is_free()
    {
        var auditBefore = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'"); // the audit log is append-only
        ApiResponse answer;
        TimeSpan took;
        await using (await HoldLockAsync("modules"))
        {
            var clock = Stopwatch.StartNew();
            answer = await _admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, "Lock test");
            took = clock.Elapsed;
        }

        var problem = answer.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ModulesLockBusy, problem.Detail);
        Assert.True(took < TimeSpan.FromSeconds(4), $"The 409 took {took.TotalSeconds:F1} s: the wait must not be repeated or retried.");
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] LIKE N'Module.%'"));
        Assert.Equal(auditBefore, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'"));

        (await _admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, "Lock test")).ShouldBeOk();
    }
}
