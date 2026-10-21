using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.3 acceptance 8 and the race the review of PL2.1 asked this slice to settle, on the real host and a real SQL Server: the purchasing module cannot be turned off while an order is
/// Draft, Submitted or Approved (409 naming the count, nothing written), Closed and Cancelled orders do not count and are kept for the next time the module is on, and an order
/// cannot be created in the instant between the flip's count and its commit (the shared <c>modules</c> lock of order creation against the flip's exclusive one).
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchasingModuleDisableTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private static string Why() => $"Purchasing disable test {Guid.NewGuid():N}"[..30];

    private Task<ApiResponse> SwitchOffAsync() => _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, Why());

    private Task<int> OpenOrdersAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder WHERE Status <= 2");

    private Task<int> ModuleAuditEventsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

    private Task<bool> IsOnAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.purchasing' AND [Value] = N'true'").ContinueWith(t => t.Result == 1);

    // ----- the refusal -----
    [Fact]
    public async Task An_open_order_refuses_the_switch_off_with_409_naming_the_count_and_nothing_is_written()
    {
        var one = await _p.DraftWorthAsync(10m);
        var audit = await ModuleAuditEventsAsync();

        var single = (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);
        await _p.DraftWorthAsync(10m);
        var held = await _p.SubmittedWorthAsync(6000m);
        var released = await _p.SubmittedWorthAsync(100m);
        var several = (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("1 purchase order is open", single.Detail);
        Assert.Equal((PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved), (held.Status, released.Status));
        Assert.Contains("4 purchase orders are open", several.Detail); // Draft, Draft, Submitted, Approved: every open state counts
        Assert.Contains("Purchasing module cannot be turned off", several.Detail);
        Assert.True(await IsOnAsync());
        Assert.Equal(audit, await ModuleAuditEventsAsync());
        Assert.Equal(PurchaseOrderStatus.Draft, (await _p.ReadAsync(one.Id)).Status); // and the module still answers
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Approved)]
    public async Task Each_open_state_alone_is_enough_to_refuse(PurchaseOrderStatus status)
    {
        var order = await _p.DraftWorthAsync(10m);
        await _p.SetStatusAsync(order, status);

        (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.True(await IsOnAsync());
    }

    [Fact]
    public async Task Orders_that_are_closed_or_cancelled_do_not_count_the_module_goes_off_and_on_again_it_finds_them_as_they_were()
    {
        var cancelled = await _p.DraftWorthAsync(10m);
        var cancelledNow = (await _p.Clerk.PurchaseOrders.CancelAsync(cancelled.Id, null, cancelled.ConcurrencyToken)).Data<PurchaseOrderDto>();
        var closed = await _p.DraftWorthAsync(20m);
        await _p.SetStatusAsync(closed, PurchaseOrderStatus.Closed);
        var audit = await ModuleAuditEventsAsync();

        (await SwitchOffAsync()).ShouldBeOk();

        Assert.False(await IsOnAsync());
        Assert.Equal(audit + 1, await ModuleAuditEventsAsync());
        (await _p.Admin.PurchaseOrders.GetAsync(cancelled.Id)).ShouldBeProblem(HttpStatusCode.NotFound); // hidden while off
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, Why())).ShouldBeOk();
        var again = await _p.ReadAsync(cancelled.Id);
        Assert.Equal((cancelledNow.No, PurchaseOrderStatus.Cancelled, cancelledNow.ConcurrencyToken), (again.No, again.Status, again.ConcurrencyToken)); // the data was kept untouched
        Assert.Equal(PurchaseOrderStatus.Closed, (await _p.ReadAsync(closed.Id)).Status);
    }

    [Fact]
    public async Task Once_the_last_open_order_is_cancelled_the_switch_off_works()
    {
        var order = await _p.DraftWorthAsync(10m);
        (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);

        (await _p.Clerk.PurchaseOrders.CancelAsync(order.Id, null, order.ConcurrencyToken)).ShouldBeOk();

        (await SwitchOffAsync()).ShouldBeOk();
        Assert.False(await IsOnAsync());
    }

    [Fact]
    public async Task Turning_the_module_on_is_never_refused_by_its_orders()
    {
        await _p.DraftWorthAsync(10m);

        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, Why())).ShouldBeOk(); // already on: a 200 that changes nothing

        Assert.True(await IsOnAsync());
    }

    // ----- the race (TOCTOU) -----
    /// <summary>Holds the <c>modules</c> lock exclusively in a transaction of its own, as a flip does from its count to its commit; committing or rolling back releases it.</summary>
    private async Task<SqlTransaction> HoldModulesLockAsync()
    {
        var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await new SqlCommand("EXEC sp_getapplock @Resource = N'modules', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0", connection, tx).ExecuteNonQueryAsync();
        return tx;
    }

    private static async Task ReleaseAsync(SqlTransaction tx, bool commit)
    {
        var connection = tx.Connection!;
        if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task An_order_created_while_a_flip_is_in_flight_waits_for_it_and_then_reads_the_switch_again_so_no_order_is_left_behind()
    {
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder");
        var product = await _p.NewProductAsync();
        var tx = await HoldModulesLockAsync();

        // The request passes the gate (the module is on in the committed row), then waits for the lock the flip holds.
        var create = _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, fixture.Reference.SupplierId).WithLine(product.Id, 1m, 5m).Build());
        await Task.Delay(1000);
        var waited = !create.IsCompleted; // asserted after the lock is let go, so a failure cannot leave it held
        await fixture.Database.ExecuteAsync("UPDATE SystemSetting SET [Value] = N'false' WHERE [Key] = N'Module.purchasing'"); // the flip's write, committed before it lets go
        await ReleaseAsync(tx, commit: true);

        var answer = await create;
        Assert.True(waited, "the create must wait for the flip that holds the modules lock");
        answer.ShouldBeProblem(HttpStatusCode.NotFound); // the switch is read again once the lock is held: a request that passed the gate earlier is refused like any other

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder"));
        Assert.Equal(0, await OpenOrdersAsync());
    }

    [Fact]
    public async Task An_order_created_while_a_flip_is_in_flight_that_then_fails_is_created_once_the_lock_is_free()
    {
        var product = await _p.NewProductAsync();
        var tx = await HoldModulesLockAsync();
        var create = _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, fixture.Reference.SupplierId).WithLine(product.Id, 1m, 5m).Build());
        await Task.Delay(500);
        var waited = !create.IsCompleted;

        await ReleaseAsync(tx, commit: false); // the flip was refused or failed: nothing changed, the module stays on

        var created = (await create).Data<PurchaseOrderDto>();
        Assert.True(waited);
        Assert.Equal(PurchaseOrderStatus.Draft, created.Status);
    }

    [Fact]
    public async Task Creates_racing_a_switch_off_never_leave_an_open_order_behind_a_module_that_is_off()
    {
        for (var round = 0; round < 12; round++)
        {
            await PurchasingScenario.ResetAsync(fixture);
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.purchasing', N'true')");
            var product = await _p.NewProductAsync();
            Func<Task<ApiResponse>> create = () => _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, fixture.Reference.SupplierId).WithLine(product.Id, 1m, 5m).Build());

            var results = await Concurrently.RunAsync<ApiResponse>([create, create, () => SwitchOffAsync(), create, create]);

            var codes = results.Select(r => (int)r.Status).ToList();
            Assert.All(codes, c => Assert.Contains(c, new[] { 200, 404, 409, 503 })); // created; module found off; flip refused by an order; a lock that stayed busy too long
            if (!await IsOnAsync()) Assert.Equal(0, await OpenOrdersAsync()); // the invariant: a module that is off has no open order
        }
    }
}
