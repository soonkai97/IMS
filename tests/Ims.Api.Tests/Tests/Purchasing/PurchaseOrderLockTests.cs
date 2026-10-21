using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.4b: the order's lock (<c>po:{id}</c>) made visible and deterministic. The test holds the lock itself (<see cref="HeldOrderLock"/>), from its own connection, starts the request, and waits until
/// SQL Server shows the request waiting for it; only then does it change what the request must see after the lock (cancel the order, add a linked receipt) and let go. A handler that does not take
/// the lock does not wait, finishes at once with the old picture and fails the first assertion. No sleeps decide the outcome, so these tests do not depend on timing luck (the probabilistic race in
/// <see cref="PurchaseOrderReceivingTests"/> is the end-to-end companion).
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan RequestEndsWithin = TimeSpan.FromSeconds(30);

    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

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

    /// <summary>
    /// Runs <paramref name="request"/> while the test holds the lock, waits until it waits, lets <paramref name="whileItWaits"/> change the world and commits. Whatever happens, the lock is let go and the
    /// request is awaited (bounded) before the method returns or throws, so a failed wait assertion cannot leave a request running into the next test.
    /// </summary>
    private async Task<ApiResponse> RunWhileLockedAsync(Guid orderId, Func<Task<ApiResponse>> request, Func<HeldOrderLock, Task> whileItWaits, string notWaitingMessage)
    {
        var held = await HeldOrderLock.TakeAsync(fixture, orderId);
        Task<ApiResponse>? running = null;
        try
        {
            running = Task.Run(request);
            Assert.True(await SomeoneWaitsForTheLockAsync(running), notWaitingMessage);
            await whileItWaits(held);
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync(); // rolls back anything not committed and lets go of the lock
            if (running is not null)
            {
                try { await running.WaitAsync(RequestEndsWithin); } catch (Exception) { /* its result is judged below, or does not matter when the wait assertion failed */ }
            }
        }
        return await running;
    }

    [Fact]
    public async Task Creating_a_receipt_waits_for_the_orders_lock_and_then_sees_an_order_that_was_cancelled_while_it_waited()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 10m, 2m, null));

        var response = await RunWhileLockedAsync(order.Id, () => _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id),
            held => held.ExecuteAsync("UPDATE PurchaseOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)PurchaseOrderStatus.Cancelled), ("@id", order.Id)), // the cancel, committed while the receipt waits
            "the request did not wait for the order's lock (it must take po:{id} before it reads the order)");

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.PurchaseOrderNotReceivable(order.No, PurchaseOrderStatus.Cancelled), problem.Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE PurchaseOrderId = (SELECT Id FROM PurchaseOrder WHERE PublicId = @id)", ("@id", order.Id)));
    }

    [Fact]
    public async Task Cancelling_an_approved_order_waits_for_the_orders_lock_and_then_sees_a_receipt_made_while_it_waited()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 10m, 2m, null));
        var receiptNo = Unique.Code("RCV");

        var response = await RunWhileLockedAsync(order.Id, () => _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken),
            held => held.ExecuteAsync("""
                INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride, PurchaseOrderId)
                SELECT @type, @no, @status, o.WarehouseId, o.CreatedBy, SYSUTCDATETIME(), 0, 0, 0, o.Id FROM PurchaseOrder o WHERE o.PublicId = @id
                """, ("@type", (int)DocumentType.Receipt), ("@status", (int)DocumentStatus.Draft), ("@no", receiptNo), ("@id", order.Id)), // the receipt that was being made when the cancel arrived
            "the cancel did not wait for the order's lock (it must take po:{id} before it looks for receipts)");

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains(receiptNo, problem.Detail);
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
