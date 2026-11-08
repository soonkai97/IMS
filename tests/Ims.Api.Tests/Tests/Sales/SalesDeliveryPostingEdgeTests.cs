using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.7 review: the corners of posting a delivery against a sales order. A reversal takes the order's lock like a posting does; a base quantity that is a rounded product never decides what is delivered; the same
/// delivery posted twice is answered "already posted"; the actions an order lists are ones the API accepts; every refusal writes nothing; the races between post, reverse, close and cancel end consistent.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesDeliveryPostingEdgeTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private StockScenario Stock => _s.Stock;

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private async Task<(ProductDto Product, SalesOrderDto Order)> OrderAsync(decimal ordered, decimal onHand, Guid? uom = null, Action<ProductBuilder>? customize = null)
    {
        var product = await _s.NewProductAsync(customize);
        if (onHand > 0) await Stock.ReceiveAsync(_s.Warehouse, new Line(product.Id, onHand, 2m));
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, ordered, 5m, uom).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        Assert.Equal(SalesOrderStatus.Approved, order.Status);
        return (product, order);
    }

    /// <summary>A delivery draft for <paramref name="qty"/> of the order's one line, submitted and, when it needs it, approved: ready to post.</summary>
    private async Task<DocumentDto> ReadyAsync(SalesOrderDto order, Guid product, decimal qty, Guid? uom = null)
    {
        var draft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var saved = (await _s.Clerk.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, IssuePurpose.Sale, null, [new DocumentLineInput(product, qty, uom, null, null, null, 1)]), draft.ConcurrencyToken)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(saved.Id, saved.ConcurrencyToken)).Data<DocumentDto>();
        if (submitted.RequiresApproval) (await Stock.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        return (await Stock.Admin.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
    }

    private Task<ApiResponse> PostAsync(DocumentDto delivery) => _s.Admin.Documents.PostAsync(delivery.Id, delivery.ConcurrencyToken);

    private async Task<int> LedgerCountAsync(Guid product) => (await Stock.LedgerAsync(product, _s.Warehouse)).Count;

    private Task<int> ClosedAuditAsync(string orderNo) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", orderNo));

    // ----- B2: a rounded base quantity decides nothing -----
    [Theory]
    [InlineData("3", "1,1,1")]
    [InlineData("1.5", "0.5,0.5,0.5")]   // 3 x 0.3333 = 0.9999 against 1.0000: dust in base units
    [InlineData("0.0003", "0.0001,0.0001,0.0001")]
    public async Task Deliveries_that_add_up_to_the_order_in_its_own_unit_post_and_close_it_whatever_rounding_does_to_the_base_quantity(string ordered, string parts)
    {
        var carton = fixture.Reference.CartonUnitId;
        var (product, order) = await OrderAsync(decimal.Parse(ordered), onHand: 10m, carton, b => b.WithConversion(carton, 0.666667m));
        var deliveries = new List<DocumentDto>();
        foreach (var part in parts.Split(',')) deliveries.Add(await ReadyAsync(order, product.Id, decimal.Parse(part), carton)); // each is within what remained when it was saved

        foreach (var delivery in deliveries) (await PostAsync(delivery)).ShouldBeOk();

        var read = await _s.ReadAsync(order.Id);
        Assert.Equal((SalesOrderStatus.Closed, SalesOrderCloseKind.Delivered, 0m), (read.Status, read.CloseKind, read.Lines[0].RemainingQty));
        Assert.DoesNotContain(DocumentActions.Deliver, read.AllowedActions);
        Assert.Equal(decimal.Parse(ordered), read.Lines[0].DeliveredQty);
        (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task One_step_over_in_the_order_lines_unit_is_refused_exactly_what_remains_posts_and_a_carton_is_judged_as_a_carton()
    {
        var carton = fixture.Reference.CartonUnitId;
        var (product, order) = await OrderAsync(10m, onHand: 200m, carton, b => b.WithConversion(carton, 12m));
        var first = await ReadyAsync(order, product.Id, 6m, carton);
        var over = await ReadyAsync(order, product.Id, 4.0833m, carton);  // 49 pieces against 48 left: one base unit over, saved while ten cartons remained
        var exact = await ReadyAsync(order, product.Id, 4m, carton);
        (await PostAsync(first)).ShouldBeOk();
        var ledger = await LedgerCountAsync(product.Id);

        var refused = await PostAsync(over);

        Assert.Contains("over_delivery", refused.Body);
        Assert.Contains("Remaining 4", refused.ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(ledger, await LedgerCountAsync(product.Id));
        (await PostAsync(exact)).ShouldBeOk();
        Assert.Equal(SalesOrderStatus.Closed, (await _s.ReadAsync(order.Id)).Status);
        Assert.Equal(200m - 120m, await Stock.OnHandAsync(product.Id, _s.Warehouse)); // 10 cartons of 12
    }

    // ----- B1: the reversal and the order's lock -----
    private async Task<bool> WaitersAsync(int atLeast, params Task[] requests)
    {
        for (var i = 0; i < 100 && requests.All(r => !r.IsCompleted); i++)
        {
            if (await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") >= atLeast) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task A_reversal_of_a_linked_delivery_waits_for_the_orders_lock_before_it_locks_any_stock()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var posted = (await PostAsync(await ReadyAsync(order, product.Id, 4m))).Data<DocumentDto>();
        await using var held = await HeldOrderLock.TakeSalesAsync(fixture, order.Id);

        var reversing = Task.Run(() => _s.Admin.Documents.ReverseAsync(posted.Id, "Delivered to the wrong customer"));
        var waited = await WaitersAsync(1, reversing);
        var spid = await held.ScalarAsync<int>("SELECT @@SPID");
        var otherLocks = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'GRANT' AND request_session_id <> @spid AND resource_database_id = DB_ID()", ("@spid", spid));
        await held.CommitAsync();
        var response = await reversing.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(waited, "the reversal did not wait for the order's lock (it must take so:{id} before the stock locks)");
        Assert.Equal(0, otherLocks);
        response.ShouldBeOk();
        Assert.Equal(0m, (await _s.ReadAsync(order.Id)).Lines[0].DeliveredQty);
    }

    [Fact]
    public async Task Twenty_five_rounds_of_reversing_one_delivery_while_posting_the_last_one_end_with_the_order_consistent_with_what_is_delivered()
    {
        for (var round = 0; round < 25; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 20m);
            var a = (await PostAsync(await ReadyAsync(order, product.Id, 4m))).Data<DocumentDto>();
            var b = await ReadyAsync(order, product.Id, 6m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Admin.Documents.ReverseAsync(a.Id, "Delivered to the wrong customer"), () => PostAsync(b)]);

            Assert.All(results, r => r.ShouldBeOk());                                    // both are legitimate, in either order
            var read = await _s.ReadAsync(order.Id);
            Assert.Equal(6m, read.Lines[0].DeliveredQty);                                 // A is back, B is out
            Assert.Equal(14m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
            Assert.Equal(14m, await Stock.LedgerSumAsync(product.Id, _s.Warehouse));
            if (read.Status == SalesOrderStatus.Closed)
            {
                // closed means the posting saw A still counted and reached 10: the reversal came after it, never before
                Assert.Equal(SalesOrderCloseKind.Delivered, read.CloseKind);
                Assert.True((await Stock.GetAsync(a.Id)).ReversedAt >= read.ClosedAt, "the order was closed on a picture the reversal had already changed");
                Assert.Equal(1, await ClosedAuditAsync(order.No));
            }
            else
            {
                Assert.Equal(SalesOrderStatus.Approved, read.Status);                     // the reversal came first: B alone did not finish the order
                Assert.Equal(0, await ClosedAuditAsync(order.No));
            }
        }
    }

    [Fact]
    public async Task Twenty_five_rounds_of_closing_short_while_reversing_the_only_delivery_never_close_on_a_picture_the_reversal_changed()
    {
        for (var round = 0; round < 25; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 20m);
            var a = (await PostAsync(await ReadyAsync(order, product.Id, 4m))).Data<DocumentDto>();

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Admin.Documents.ReverseAsync(a.Id, "Delivered to the wrong customer"), () => _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken)]);

            results[0].ShouldBeOk();
            var read = await _s.ReadAsync(order.Id);
            if (results[1].Status == HttpStatusCode.OK)
            {
                // the close was made while something was delivered: the reversal ran after it
                Assert.True((await Stock.GetAsync(a.Id)).ReversedAt >= read.ClosedAt);
                Assert.Equal(SalesOrderStatus.Closed, read.Status);
            }
            else
            {
                results[1].ShouldBeProblem(HttpStatusCode.Conflict);                     // nothing delivered any more: cancel it instead
                Assert.Equal(SalesOrderStatus.Approved, read.Status);
                Assert.Equal(0, await ClosedAuditAsync(order.No));
            }
        }
    }

    [Fact]
    public async Task Posting_a_delivery_while_the_order_is_being_closed_short_or_cancelled_always_posts_cancel_is_always_refused_and_a_close_only_succeeds_after_the_post()
    {
        for (var round = 0; round < 10; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 20m);
            (await PostAsync(await ReadyAsync(order, product.Id, 3m))).ShouldBeOk();       // something delivered, so close short is allowed in principle
            var b = await ReadyAsync(order, product.Id, 4m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(b), () => _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", order.ConcurrencyToken), () => _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", order.ConcurrencyToken)]);

            results[0].ShouldBeOk();
            Assert.NotEqual(HttpStatusCode.OK, results[2].Status);                           // cancel is refused whether the delivery is still open or already posted
            var read = await _s.ReadAsync(order.Id);
            Assert.Equal(7m, read.Lines[0].DeliveredQty);
            // a close short is refused while the delivery is open and legitimate once it has posted (a partial post does not move the order's token): it never closes on a picture without the delivery
            Assert.Equal(results[1].Status == HttpStatusCode.OK ? (SalesOrderStatus.Closed, SalesOrderCloseKind.Short) : (SalesOrderStatus.Approved, (SalesOrderCloseKind?)null), (read.Status, read.CloseKind));
        }
    }

    // ----- minor: the same delivery twice -----
    [Fact]
    public async Task The_same_delivery_posted_twice_at_once_is_one_200_and_one_answer_that_it_is_already_posted_not_an_over_delivery()
    {
        for (var round = 0; round < 10; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 20m);
            var ready = await ReadyAsync(order, product.Id, 10m); // the whole order: counted twice it WOULD be over, which is what a stale posting would wrongly say

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(ready), () => PostAsync(ready)]);

            Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
            var loser = results.Single(r => r.Status != HttpStatusCode.OK);
            loser.ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.DoesNotContain("over_delivery", loser.Body);       // not judged as a second delivery of the same goods
            Assert.Contains("document_changed", loser.Body);         // the delivery changed under it: the first posting got there first
            Assert.Equal(10m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
            Assert.Equal(10m, (await _s.ReadAsync(order.Id)).Lines[0].DeliveredQty);
        }
    }

    [Fact]
    public async Task The_loser_of_two_drafts_of_six_against_ten_is_told_over_delivery_and_the_order_is_still_open()
    {
        for (var round = 0; round < 10; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 12m);
            var a = await ReadyAsync(order, product.Id, 6m);
            var b = await ReadyAsync(order, product.Id, 6m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(a), () => PostAsync(b)]);

            var loser = results.Single(r => r.Status != HttpStatusCode.OK);
            loser.ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Contains("over_delivery", loser.Body);
            Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
        }
    }

    // ----- minor: what is listed is accepted -----
    [Fact]
    public async Task Cancel_and_close_are_listed_only_when_the_api_will_accept_them_and_the_endpoints_agree()
    {
        var (product, order) = await OrderAsync(20m, onHand: 50m);
        Assert.Contains(DocumentActions.Cancel, (await _s.ReadAsync(order.Id)).AllowedActions);          // nothing delivered yet
        Assert.DoesNotContain(DocumentActions.Close, (await _s.ReadAsync(order.Id)).AllowedActions);

        var open = await ReadyAsync(order, product.Id, 4m);
        var withOpen = await _s.ReadAsync(order.Id);
        Assert.DoesNotContain(DocumentActions.Cancel, withOpen.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Close, withOpen.AllowedActions);
        (await _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", withOpen.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        (await PostAsync(open)).ShouldBeOk();
        var withPosted = await _s.ReadAsync(order.Id);
        Assert.DoesNotContain(DocumentActions.Cancel, withPosted.AllowedActions);                         // a posted delivery blocks cancel
        Assert.Contains(DocumentActions.Close, withPosted.AllowedActions);
        (await _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", withPosted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", withPosted.ConcurrencyToken)).ShouldBeOk();
    }

    // ----- minor: the moment of the close -----
    [Fact]
    public async Task The_order_is_closed_at_the_moment_its_last_delivery_was_posted()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);

        var posted = (await PostAsync(await ReadyAsync(order, product.Id, 10m))).Data<DocumentDto>();

        Assert.Equal(posted.PostedAt, (await _s.ReadAsync(order.Id)).ClosedAt);
    }

    // ----- permission, scope, stale token at post -----
    [Fact]
    public async Task Posting_a_linked_delivery_needs_the_post_right_a_warehouse_the_caller_can_see_and_a_current_token()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var ready = await ReadyAsync(order, product.Id, 4m);
        var ledger = await LedgerCountAsync(product.Id);

        (await Forged([Permissions.StockRead, Permissions.DocumentCreate]).Documents.PostAsync(ready.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await _s.Admin.Documents.PostAsync(ready.Id, "stale-token")).ShouldBeProblem(HttpStatusCode.Conflict);
        var other = await _s.NewWarehouseAsync();
        var (scoped, _) = await new AccountFactory(fixture).SignInAsync(await new AccountFactory(fixture).CreateAsync(Roles.Supervisor, [other.Id]));
        (await scoped.Documents.PostAsync(ready.Id)).ShouldBe(HttpStatusCode.NotFound);

        Assert.Equal(ledger, await LedgerCountAsync(product.Id));
        (await PostAsync(ready)).ShouldBeOk();
    }

    // ----- real sequences -----
    [Fact]
    public async Task Post_then_reverse_then_close_short_is_409_nothing_delivered_and_writes_no_audit()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var posted = (await PostAsync(await ReadyAsync(order, product.Id, 6m))).Data<DocumentDto>();
        (await _s.Admin.Documents.ReverseAsync(posted.Id, "Delivered to the wrong customer")).ShouldBeOk();

        var problem = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", (await _s.ReadAsync(order.Id)).ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNothingDelivered(order.No), problem.Detail);
        Assert.Equal(0, await ClosedAuditAsync(order.No));
    }

    [Fact]
    public async Task Post_six_then_a_new_draft_blocks_the_close_naming_it_and_posting_the_draft_lets_the_close_through()
    {
        var (product, order) = await OrderAsync(20m, onHand: 50m);
        (await PostAsync(await ReadyAsync(order, product.Id, 6m))).ShouldBeOk();
        var waiting = await ReadyAsync(order, product.Id, 4m);

        var blocked = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", (await _s.ReadAsync(order.Id)).ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains(waiting.No, blocked.Detail);
        (await PostAsync(waiting)).ShouldBeOk();

        var closed = (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", (await _s.ReadAsync(order.Id)).ConcurrencyToken)).Data<SalesOrderDto>();
        Assert.Equal((SalesOrderStatus.Closed, SalesOrderCloseKind.Short, 10m), (closed.Status, closed.CloseKind, closed.Lines[0].DeliveredQty));
    }

    [Fact]
    public async Task A_reversal_after_the_order_closed_as_delivered_gives_the_quantity_back_writes_a_reversal_row_and_no_second_close_and_a_new_delivery_is_409()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var posted = (await PostAsync(await ReadyAsync(order, product.Id, 10m))).Data<DocumentDto>();
        var ledger = await Stock.LedgerAsync(product.Id, _s.Warehouse);
        Assert.Equal(1, await ClosedAuditAsync(order.No));

        (await _s.Admin.Documents.ReverseAsync(posted.Id, "Delivered to the wrong customer")).ShouldBeOk();

        var after = await Stock.LedgerAsync(product.Id, _s.Warehouse);
        Assert.Equal(ledger.Count + 1, after.Count);
        Assert.Contains(after, r => r.QtyDelta == 10m && r.Reason == LedgerReason.Reversal);     // the opposite entry
        Assert.Equal(20m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
        var read = await _s.ReadAsync(order.Id);
        Assert.Equal((SalesOrderStatus.Closed, 0m, 10m), (read.Status, read.Lines[0].DeliveredQty, read.Lines[0].RemainingQty));
        Assert.Equal(1, await ClosedAuditAsync(order.No));                                        // never closed twice, never reopened
        (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    // ----- the close endpoint -----
    [Fact]
    public async Task Every_refusal_of_a_close_writes_no_audit_row_and_leaves_the_order_as_it_was()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        (await PostAsync(await ReadyAsync(order, product.Id, 4m))).ShouldBeOk();
        var current = await _s.ReadAsync(order.Id);

        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", "stale-token")).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "no", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var openDelivery = await ReadyAsync(order, product.Id, 2m);
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict); // a delivery is open
        (await _s.Staff.SalesOrders.CloseAsync(order.Id, "Customer will take no more", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Forged([Permissions.DocumentCreate]).SalesOrders.CloseAsync(order.Id, "Customer will take no more", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden); // neither so.manage nor so.read
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.sales'"); // off, without the reset helper that would cancel the open order
        (await _s.Clerk.SalesOrders.CloseAsync(order.Id, "Customer will take no more", current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.sales', N'true')");

        Assert.Equal(0, await ClosedAuditAsync(order.No));
        Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
        Assert.NotEqual(DocumentStatus.Posted, (await Stock.GetAsync(openDelivery.Id)).Status);
    }

    // ----- ledger unchanged on every refusal of a post -----
    [Fact]
    public async Task Every_refusal_of_a_post_leaves_the_ledger_and_the_balance_as_they_were()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var a = await ReadyAsync(order, product.Id, 6m);
        var b = await ReadyAsync(order, product.Id, 6m);
        (await PostAsync(a)).ShouldBeOk();
        var ledger = await LedgerCountAsync(product.Id);

        (await PostAsync(b)).ShouldBeProblem(HttpStatusCode.Conflict);                                    // over
        (await _s.Admin.Documents.PostAsync(b.Id, "stale")).ShouldBeProblem(HttpStatusCode.Conflict);
        await _s.SetStatusAsync(order, SalesOrderStatus.Cancelled);
        (await PostAsync(b)).ShouldBeProblem(HttpStatusCode.Conflict);                                    // not approved
        await _s.TurnOffAsync();
        (await PostAsync(b)).ShouldBeProblem(HttpStatusCode.Conflict);                                    // module off

        Assert.Equal(ledger, await LedgerCountAsync(product.Id));
        Assert.Equal(14m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
    }
}
