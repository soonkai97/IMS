using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.4b: the order's lock (<c>po:{id}</c>) made visible and deterministic. The test holds the lock itself, from its own connection, starts the request, and waits until SQL Server shows the
/// request waiting for it; only then does it change what the request must see after the lock (cancel the order, add a linked receipt) and let go. A handler that does not take the lock does
/// not wait, finishes at once with the old picture and fails the first assertion. No sleeps decide the outcome, so these tests do not depend on timing luck (the probabilistic race in
/// <see cref="PurchaseOrderReceivingTests"/> is the end-to-end companion).
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    /// <summary>Holds <c>po:{id}</c> in a transaction of its own until <see cref="CommitAsync"/>; work done through it is invisible to others until then.</summary>
    private sealed class HeldOrderLock : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        private HeldOrderLock(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<HeldOrderLock> TakeAsync(ApiFixture fixture, Guid orderId)
        {
            var key = await fixture.Database.ScalarAsync<int>("SELECT Id FROM PurchaseOrder WHERE PublicId = @id", ("@id", orderId));
            var connection = new SqlConnection(fixture.Database.ConnectionString);
            await connection.OpenAsync();
            var held = new HeldOrderLock(connection, (SqlTransaction)await connection.BeginTransactionAsync());
            var granted = await held.ScalarAsync<int>($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'po:{key}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r");
            Assert.True(granted >= 0, "the test could not take the order's lock");
            return held;
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var command = new SqlCommand(sql, _connection, _transaction);
            return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, _connection, _transaction);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public Task CommitAsync() => _transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>True once SQL Server shows a request waiting for an application lock in this test database (the only waiter there is the request under test).</summary>
    private async Task<bool> SomeoneWaitsForTheLockAsync(Task request)
    {
        for (var i = 0; i < 100 && !request.IsCompleted; i++)
        {
            if (await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task Creating_a_receipt_waits_for_the_orders_lock_and_then_sees_an_order_that_was_cancelled_while_it_waited()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 10m, 2m, null));
        await using var held = await HeldOrderLock.TakeAsync(fixture, order.Id);

        var create = Task.Run(() => _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id));

        Assert.True(await SomeoneWaitsForTheLockAsync(create), "the request did not wait for the order's lock (it must take po:{id} before it reads the order)");
        await held.ExecuteAsync("UPDATE PurchaseOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)PurchaseOrderStatus.Cancelled), ("@id", order.Id)); // the cancel, committed while the receipt waits
        await held.CommitAsync();

        var problem = (await create).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.PurchaseOrderNotReceivable(order.No, PurchaseOrderStatus.Cancelled), problem.Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE PurchaseOrderId = (SELECT Id FROM PurchaseOrder WHERE PublicId = @id)", ("@id", order.Id)));
    }

    [Fact]
    public async Task Cancelling_an_approved_order_waits_for_the_orders_lock_and_then_sees_a_receipt_made_while_it_waited()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 10m, 2m, null));
        await using var held = await HeldOrderLock.TakeAsync(fixture, order.Id);

        var cancel = Task.Run(() => _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken));

        Assert.True(await SomeoneWaitsForTheLockAsync(cancel), "the cancel did not wait for the order's lock (it must take po:{id} before it looks for receipts)");
        await held.ExecuteAsync("""
            INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride, PurchaseOrderId)
            SELECT 1, N'RCV-LOCK-0001', 0, o.WarehouseId, o.CreatedBy, SYSUTCDATETIME(), 0, 0, 0, o.Id FROM PurchaseOrder o WHERE o.PublicId = @id
            """, ("@id", order.Id)); // the receipt that was being made when the cancel arrived
        await held.CommitAsync();

        var problem = (await cancel).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("RCV-LOCK-0001", problem.Detail);
        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Cancelling_a_draft_order_does_not_take_the_lock_because_no_receipt_can_exist_yet()
    {
        var draft = await _p.CreateAsync(_p.Warehouse, (await _p.NewProductAsync()).Id, _p.Clerk);
        await using var held = await HeldOrderLock.TakeAsync(fixture, draft.Id);

        var cancelled = (await _p.Clerk.PurchaseOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<PurchaseOrderDto>();

        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
    }
}
