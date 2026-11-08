using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.7: closing an Approved order short. Allowed for a holder of so.manage, with a reason of 5 or more characters, only once something has been delivered (with nothing delivered the answer is to cancel),
/// and not while a delivery of the order is Draft, Submitted or Approved. The <c>close</c> action is offered by the same rule.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderCloseTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    /// <summary>An Approved order of 10 with one delivery of <paramref name="qty"/> in <paramref name="status"/> (Posted is set the way a post leaves it).</summary>
    private async Task<(SalesOrderDto Order, DocumentDto Delivery)> OrderWithDeliveryAsync(DocumentStatus status, decimal qty = 6m)
    {
        var product = (await _s.NewProductAsync()).Id;
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product, 10m, 5m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        var delivery = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = @s WHERE No = @no", ("@s", (int)status), ("@no", delivery.No));
        await fixture.Database.ExecuteAsync("UPDATE StockDocumentLine SET Qty = @q, BaseQty = @q WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no)", ("@q", qty), ("@no", delivery.No));
        return (order, delivery);
    }

    [Fact]
    public async Task A_holder_of_so_manage_closes_an_order_short_once_something_was_delivered_and_what_was_delivered_stays()
    {
        var (order, _) = await OrderWithDeliveryAsync(DocumentStatus.Posted);

        var closed = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "  Customer will take no more  ", order.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Closed, SalesOrderCloseKind.Short, "Customer will take no more"), (closed.Status, closed.CloseKind, closed.CloseReason));
        Assert.Equal(6m, closed.Lines[0].DeliveredQty);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", order.No)));
        Assert.DoesNotContain(DocumentActions.Deliver, closed.AllowedActions);
    }

    [Fact]
    public async Task With_nothing_delivered_the_answer_is_cancel_it_instead_409()
    {
        var product = (await _s.NewProductAsync()).Id;
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product, 10m, 5m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        var problem = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNothingDelivered(order.No), problem.Detail);
        Assert.DoesNotContain(DocumentActions.Close, (await _s.ReadAsync(order.Id, _s.Clerk)).AllowedActions);
        Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    public async Task A_delivery_still_waiting_blocks_the_close_409_naming_it_and_post_or_cancel_clears_the_way(DocumentStatus status)
    {
        var (order, waiting) = await OrderWithDeliveryAsync(status, qty: 4m);
        // something was delivered earlier: a second, posted delivery of 3
        var earlier = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = 3 WHERE No = @no", ("@no", earlier.No));
        await fixture.Database.ExecuteAsync("UPDATE StockDocumentLine SET Qty = 3, BaseQty = 3 WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no)", ("@no", earlier.No));

        var problem = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderHasOpenDeliveries(order.No, waiting.No, status, 1), problem.Detail);
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = 5 WHERE No = @no", ("@no", waiting.No)); // cancelled
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)).ShouldBeOk();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    public async Task A_missing_or_short_reason_is_refused_and_changes_nothing(string? reason)
    {
        var (order, _) = await OrderWithDeliveryAsync(DocumentStatus.Posted);

        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, reason, order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Only_a_holder_of_so_manage_may_close_and_the_close_action_follows_the_same_rule()
    {
        var (order, _) = await OrderWithDeliveryAsync(DocumentStatus.Posted);

        Assert.Contains(DocumentActions.Close, (await _s.ReadAsync(order.Id, _s.Clerk)).AllowedActions);
        Assert.Contains(DocumentActions.Close, (await _s.ReadAsync(order.Id, _s.Supervisor)).AllowedActions);
        Assert.DoesNotContain(DocumentActions.Close, (await _s.ReadAsync(order.Id, _s.Staff)).AllowedActions); // so.read only
        (await _s.Staff.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)).ShouldBeOk();
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public async Task An_order_that_is_not_approved_cannot_be_closed_409_naming_the_status(SalesOrderStatus status)
    {
        var order = await _s.DraftWorthAsync(10m);
        await _s.SetStatusAsync(order, status);
        var current = await _s.ReadAsync(order.Id);

        var problem = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.SalesOrderCannotClose(status), problem.Detail);
    }

    [Fact]
    public async Task A_close_needs_the_version_token()
    {
        var (order, _) = await OrderWithDeliveryAsync(DocumentStatus.Posted);

        (await _s.Clerk.SalesOrders.TransitionWithoutIfMatchAsync(order.Id, "close", new CloseSalesOrderRequest("Customer will take no more"))).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
    }
}
