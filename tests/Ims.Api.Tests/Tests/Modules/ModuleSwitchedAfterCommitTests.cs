using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Modules;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// DDD-14 against real SQL Server: a module flip raises <see cref="ModuleSwitched"/>; the audit subscriber stages its row INSIDE the transaction (it commits or rolls back with the flip, AUTH-03) and the cache
/// invalidation runs AFTER the commit, once (docs/architecture/ddd-roadmap.md 2.5). The host is the real one, so the real subscriber and the real after-commit handler run; only the module switches are wrapped, to
/// count the drops and to see whether a transaction was still open. The unit of work is driven directly with a module row of a code no catalog knows, so no real module changes state; the row is deleted afterwards.
/// <c>ModulesApiTests</c> proves the same flip through the HTTP pipeline, and <c>DomainEventDispatchTests</c> proves the dispatcher with test-only handlers.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleSwitchedAfterCommitTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly SwitchProbe _probe = new();
    private readonly string _code = Unique.Code("zm");
    private WebApplicationFactory<Program> _host = null!;

    private string Key => SettingKeys.ForModule(_code);

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(b => b.ConfigureServices(services =>
        {
            services.AddSingleton(_probe);
            services.AddScoped<IModuleSwitches, CountingSwitches>();   // the last registration wins: the use case and the after-commit handler share this one
        }));
        _ = _host.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = @k", ("@k", Key));
        await _host.DisposeAsync();
    }

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    private static Reason Why() => Reason.Of("Go live on Monday", Messages.ModuleReasonRequired);

    private Task<int> RowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = @k", ("@k", Key));

    private Task<int> AuditRowsAsync() =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @k", ("@a", AuditActions.ModuleEnabled), ("@t", AuditEntities.SystemSetting), ("@k", Key));

    [Fact]
    public async Task A_flip_commits_its_row_and_its_audit_row_and_drops_the_cached_switches_once_after_the_commit()
    {
        var dropsBeforeCommit = -1;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                sp.GetRequiredService<IRepository<SystemSetting>>().Add(SystemSetting.CreateModuleSwitch(_code, enabled: true, Why(), rolesRegainingGrants: 0));
                await uow.SaveChangesAsync();   // the audit subscriber runs here, inside the transaction
                dropsBeforeCommit = _probe.Drops;
                return 0;
            });
        });

        Assert.Equal(0, dropsBeforeCommit);                       // a save, even one that dispatched the event, does not drop the cache: the transaction is still open
        Assert.Equal(1, _probe.Drops);
        Assert.Equal([false], _probe.InTransaction);              // when it ran, nothing was open any more: the flip was committed
        Assert.Equal(1, await RowsAsync());
        Assert.Equal(1, await AuditRowsAsync());
    }

    [Fact]
    public async Task A_retried_attempt_drops_the_cache_not_for_the_discarded_attempt_but_once_overall_and_leaves_one_audit_row()
    {
        var attempts = 0;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                _probe.AttemptAtDrop = () => attempts;
                sp.GetRequiredService<IRepository<SystemSetting>>().Add(SystemSetting.CreateModuleSwitch(_code, enabled: true, Why(), rolesRegainingGrants: 0));   // built inside the body: a retry builds it again
                await uow.SaveChangesAsync();
                if (attempts == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);   // the first attempt is rolled back whole, its audit row included
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, _probe.Drops);
        Assert.Equal([2], _probe.Attempts);                       // 0 times for the discarded attempt, once overall
        Assert.Equal(1, await RowsAsync());
        Assert.Equal(1, await AuditRowsAsync());
    }

    [Fact]
    public async Task A_use_case_that_fails_after_its_save_drops_nothing_and_leaves_no_row_and_no_audit_row()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync<int>(async () =>
            {
                sp.GetRequiredService<IRepository<SystemSetting>>().Add(SystemSetting.CreateModuleSwitch(_code, enabled: true, Why(), rolesRegainingGrants: 0));
                await uow.SaveChangesAsync();
                throw new InvalidOperationException("the use case failed after its save");
            });
        }));

        Assert.Equal(0, _probe.Drops);
        Assert.Equal(0, await RowsAsync());
        Assert.Equal(0, await AuditRowsAsync());
    }

    /// <summary>What the wrapped switches saw: how many times the cache was dropped, whether a transaction was open then, and which attempt of the use case was running.</summary>
    public sealed class SwitchProbe
    {
        private readonly List<bool> _inTransaction = [];
        private readonly List<int> _attempts = [];

        public int Drops => _inTransaction.Count;
        public IReadOnlyList<bool> InTransaction => _inTransaction;
        public IReadOnlyList<int> Attempts => _attempts;
        public Func<int>? AttemptAtDrop { get; set; }

        public void Dropped(bool transactionOpen)
        {
            _inTransaction.Add(transactionOpen);
            _attempts.Add(AttemptAtDrop?.Invoke() ?? 0);
        }
    }

    private sealed class CountingSwitches(IReadRepository<SystemSetting> settings, IServiceProvider services, SwitchProbe probe) : IModuleSwitches
    {
        private readonly ModuleSwitches _inner = new(settings);

        public Task<bool> IsEnabledAsync(string code, CancellationToken ct) => _inner.IsEnabledAsync(code, ct);
        public Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct) => _inner.EnabledAsync(ct);

        public void Invalidate()
        {
            probe.Dropped(services.GetRequiredService<AppDbContext>().Database.CurrentTransaction is not null);
            _inner.Invalidate();
        }
    }
}
