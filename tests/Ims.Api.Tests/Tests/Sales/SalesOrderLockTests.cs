using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.6: the order's lock (<c>so:{id}</c>) made visible and deterministic, the twin of <c>PurchaseOrderLockTests</c>. The test holds the lock itself (<see cref="HeldOrderLock"/>), from its own connection, starts the
/// request, and waits until SQL Server shows the request waiting for it; only then does it change what the request must see after the lock (cancel the order, add a linked delivery) and let go. A handler that
/// does not take the lock does not wait, finishes at once with the old picture and fails the first assertion. No sleep decides the outcome. (Mutation-proven: with the lock call removed from the handler each
/// of the two waiting tests fails on that first assertion.)
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan RequestEndsWithin = TimeSpan.FromSeconds(30);

    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private async Task<SalesOrderDto> ApprovedAsync()
    {
        var product = await _s.NewProductAsync();
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 10m, 2m).Build())).Data<SalesOrderDto>();
        return (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
    }

    private async Task<bool> SomeoneWaitsForTheLockAsync(Task request)
    {
        for (var i = 0; i < 100 && !request.IsCompleted; i++)
        {
            if (await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private async Task<ApiResponse> RunWhileLockedAsync(Guid orderId, Func<Task<ApiResponse>> request, Func<HeldOrderLock, Task> whileItWaits, string notWaitingMessage)
    {
        var held = await HeldOrderLock.TakeSalesAsync(fixture, orderId);
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
                try { await running.WaitAsync(RequestEndsWithin); } catch (Exception) { /* judged below, or does not matter when the wait assertion failed */ }
            }
        }
        return await running;
    }

    [Fact]
    public async Task Creating_a_delivery_waits_for_the_orders_lock_and_then_sees_an_order_that_was_cancelled_while_it_waited()
    {
        var order = await ApprovedAsync();

        var response = await RunWhileLockedAsync(order.Id, () => _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id),
            held => held.ExecuteAsync("UPDATE SalesOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)SalesOrderStatus.Cancelled), ("@id", order.Id)), // the cancel, committed while the delivery waits
            "the request did not wait for the order's lock (it must take so:{id} before it reads the order)");

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, SalesOrderStatus.Cancelled), response.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId = (SELECT Id FROM SalesOrder WHERE PublicId = @id)", ("@id", order.Id)));
    }

    [Fact]
    public async Task Cancelling_an_approved_order_waits_for_the_orders_lock_and_then_sees_a_delivery_made_while_it_waited()
    {
        var order = await ApprovedAsync();
        var deliveryNo = Unique.Code("ISS");

        var response = await RunWhileLockedAsync(order.Id, () => _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", order.ConcurrencyToken),
            held => held.ExecuteAsync("""
                INSERT StockDocument (Type, No, Status, WarehouseId, CreatedBy, CreatedAt, RequiresApproval, IsLarge, RequiresOverride, SalesOrderId)
                SELECT @type, @no, @status, o.WarehouseId, o.CreatedBy, SYSUTCDATETIME(), 0, 0, 0, o.Id FROM SalesOrder o WHERE o.PublicId = @id
                """, ("@type", (int)DocumentType.Issue), ("@status", (int)DocumentStatus.Draft), ("@no", deliveryNo), ("@id", order.Id)), // the delivery that was being made when the cancel arrived
            "the cancel did not wait for the order's lock (it must take so:{id} before it looks for deliveries)");

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains(deliveryNo, problem.Detail);
        Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Posting_a_linked_delivery_waits_for_the_orders_lock_before_any_stock_lock_and_then_sees_an_order_that_was_cancelled_while_it_waited()
    {
        var product = await _s.NewProductAsync();
        await _s.Stock.ReceiveAsync(_s.Warehouse, new Line(product.Id, 20m, 2m));
        var draftOrder = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 10m, 2m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draftOrder.Id, draftOrder.ConcurrencyToken)).Data<SalesOrderDto>();
        var delivery = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<Application.Features.Documents.DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(delivery.Id, delivery.ConcurrencyToken)).Data<Application.Features.Documents.DocumentDto>();
        if (submitted.RequiresApproval) (await _s.Stock.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        var ready = (await _s.Admin.Documents.GetAsync(submitted.Id)).Data<Application.Features.Documents.DocumentDto>();

        var response = await RunWhileLockedAsync(order.Id, () => _s.Admin.Documents.PostAsync(ready.Id, ready.ConcurrencyToken),
            async held =>
            {
                // lock order: the posting holds no application lock yet (no stock lock) while it waits for the order's
                var spid = await held.ScalarAsync<int>("SELECT @@SPID");
                Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND request_session_id <> @spid AND resource_database_id = DB_ID()", ("@spid", spid)));
                await held.ExecuteAsync("UPDATE SalesOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)SalesOrderStatus.Cancelled), ("@id", order.Id)); // cancelled while the posting waited
            },
            "the posting did not wait for the order's lock (it must take so:{id} before the stock locks)");

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, SalesOrderStatus.Cancelled), response.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(20m, await _s.Stock.OnHandAsync(product.Id, _s.Warehouse));
    }

    [Fact]
    public async Task Cancelling_a_draft_order_does_not_take_the_lock_because_no_delivery_can_exist_yet()
    {
        var draft = await _s.DraftWorthAsync(10m);
        await using var held = await HeldOrderLock.TakeSalesAsync(fixture, draft.Id);

        var cancelled = (await _s.Clerk.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
    }
}
