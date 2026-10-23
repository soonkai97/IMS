using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// The sales module cannot be turned off while an order is Draft, Submitted or Approved (409 naming the count, nothing written); Closed and Cancelled orders do not count and are kept for the
/// next time the module is on; and an order cannot be created in the instant between the flip's count and its commit (the shared <c>modules</c> lock of order creation against the flip's exclusive one).
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesModuleDisableTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private static string Why() => $"Sales disable test {Guid.NewGuid():N}"[..30];

    private Task<ApiResponse> SwitchOffAsync() => _s.Admin.Modules.SwitchAsync(ModuleCodes.Sales, false, Why());

    private Task<int> OpenOrdersAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE Status <= 2");

    private Task<int> ModuleAuditEventsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

    private async Task<bool> IsOnAsync() => await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.sales' AND [Value] = N'true'") == 1;

    private async Task<SalesOrderDto> DraftAsync() => await _s.CreateAsync(_s.Reference.DefaultWarehouseId, (await _s.NewProductAsync()).Id, _s.Clerk);

    [Fact]
    public async Task An_open_order_refuses_the_switch_off_with_409_naming_the_count_and_nothing_is_written()
    {
        await DraftAsync();
        var audit = await ModuleAuditEventsAsync();

        var single = (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);
        await DraftAsync();
        var several = (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("1 sales order is open", single.Detail);
        Assert.Contains("2 sales orders are open", several.Detail);
        Assert.True(await IsOnAsync());
        Assert.Equal(audit, await ModuleAuditEventsAsync());
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Approved)]
    public async Task Each_open_state_alone_is_enough_to_refuse(SalesOrderStatus status)
    {
        await _s.SetStatusAsync(await DraftAsync(), status);

        (await SwitchOffAsync()).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.True(await IsOnAsync());
    }

    [Fact]
    public async Task Orders_that_are_closed_or_cancelled_do_not_count_the_module_goes_off_and_on_again_it_finds_them_as_they_were()
    {
        var cancelled = await DraftAsync();
        await _s.SetStatusAsync(cancelled, SalesOrderStatus.Cancelled);
        var closed = await DraftAsync();
        await _s.SetStatusAsync(closed, SalesOrderStatus.Closed);
        var audit = await ModuleAuditEventsAsync();

        (await SwitchOffAsync()).ShouldBeOk();

        Assert.False(await IsOnAsync());
        Assert.Equal(audit + 1, await ModuleAuditEventsAsync());
        (await _s.Admin.SalesOrders.GetAsync(cancelled.Id)).ShouldBeProblem(HttpStatusCode.NotFound); // hidden while off
        (await _s.Admin.Modules.SwitchAsync(ModuleCodes.Sales, true, Why())).ShouldBeOk();
        Assert.Equal(SalesOrderStatus.Cancelled, (await _s.Admin.SalesOrders.GetAsync(cancelled.Id)).Data<SalesOrderDto>().Status);
        Assert.Equal(SalesOrderStatus.Closed, (await _s.Admin.SalesOrders.GetAsync(closed.Id)).Data<SalesOrderDto>().Status);
    }

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
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder");
        var product = await _s.NewProductAsync();
        var tx = await HoldModulesLockAsync();

        // The request passes the gate (the module is on in the committed row), then waits for the lock the flip holds.
        var create = _s.Admin.SalesOrders.CreateAsync(_s.Request(_s.Reference.DefaultWarehouseId, product.Id).Build());
        await Task.Delay(1000);
        var waited = !create.IsCompleted; // asserted after the lock is let go, so a failure cannot leave it held
        await fixture.Database.ExecuteAsync("UPDATE SystemSetting SET [Value] = N'false' WHERE [Key] = N'Module.sales'"); // the flip's write, committed before it lets go
        await ReleaseAsync(tx, commit: true);

        var answer = await create;
        Assert.True(waited, "the create must wait for the flip that holds the modules lock");
        answer.ShouldBeProblem(HttpStatusCode.NotFound); // the switch is read again once the lock is held

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder"));
        Assert.Equal(0, await OpenOrdersAsync());
    }

    [Fact]
    public async Task Creates_racing_a_switch_off_never_leave_an_open_order_behind_a_module_that_is_off()
    {
        for (var round = 0; round < 12; round++)
        {
            await SalesScenario.ResetAsync(fixture);
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.sales', N'true')");
            var product = await _s.NewProductAsync();
            Func<Task<ApiResponse>> create = () => _s.Admin.SalesOrders.CreateAsync(_s.Request(_s.Reference.DefaultWarehouseId, product.Id).Build());

            var results = await Concurrently.RunAsync<ApiResponse>([create, create, () => SwitchOffAsync(), create, create]);

            Assert.All(results.Select(r => (int)r.Status), c => Assert.Contains(c, new[] { 200, 404, 409, 503 })); // created; module found off; flip refused by an order; a lock that stayed busy too long
            if (!await IsOnAsync()) Assert.Equal(0, await OpenOrdersAsync()); // the invariant: a module that is off has no open order
        }
    }
}
