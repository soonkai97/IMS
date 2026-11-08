using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.7 on a real SQL Server: posting a delivery created from a sales order. The ledger and the balances are exactly what an unlinked issue of the same lines gives; the order's lock comes first and the check
/// against what remains is made under it (the one at save is advice); a delivery that would take a line over what remains is a 409 that writes nothing; the order closes itself when the last quantity goes out;
/// a reversal gives the quantity back and never reopens it; stock shortage and the negative-stock override behave as for any issue.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesDeliveryPostingTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private StockScenario Stock => _s.Stock;

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    /// <summary>A product with <paramref name="onHand"/> in the default warehouse and an Approved order for <paramref name="ordered"/> of it (released at once: below every rule).</summary>
    private async Task<(ProductDto Product, SalesOrderDto Order)> OrderAsync(decimal ordered, decimal onHand)
    {
        var product = await _s.NewProductAsync();
        if (onHand > 0) await Stock.ReceiveAsync(_s.Warehouse, new Line(product.Id, onHand, 2m));
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, ordered, 5m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        Assert.Equal(SalesOrderStatus.Approved, order.Status);
        return (product, order);
    }

    /// <summary>A delivery draft for <paramref name="qty"/> of the order's one line, submitted and, when it needs it, approved: ready to post.</summary>
    private async Task<DocumentDto> ReadyAsync(SalesOrderDto order, Guid product, decimal qty)
    {
        var draft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var saved = (await _s.Clerk.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, IssuePurpose.Sale, null, [new DocumentLineInput(product, qty, null, null, null, null, 1)]), draft.ConcurrencyToken)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(saved.Id, saved.ConcurrencyToken)).Data<DocumentDto>();
        if (submitted.RequiresApproval) (await Stock.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        return (await Stock.Admin.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
    }

    private Task<ApiResponse> PostAsync(DocumentDto delivery) => _s.Admin.Documents.PostAsync(delivery.Id, delivery.ConcurrencyToken);

    // ----- parity with an unlinked issue -----
    [Fact]
    public async Task A_linked_delivery_leaves_the_same_ledger_row_balance_and_average_cost_as_an_unlinked_issue_of_the_same_lines_and_the_order_shows_the_quantity()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var twin = await _s.NewProductAsync();
        await Stock.ReceiveAsync(_s.Warehouse, new Line(twin.Id, 20m, 2m));
        var linked = await ReadyAsync(order, product.Id, 4m);

        var posted = (await PostAsync(linked)).Data<DocumentDto>();
        await Stock.IssueAsync(_s.Warehouse, new Line(twin.Id, 4m));

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        (decimal, decimal?, decimal) Shape(IEnumerable<LedgerRow> rows) => rows.Where(r => r.QtyDelta < 0).Select(r => (r.QtyDelta, r.BalanceAfter, r.UnitCost!.Value)).Single();
        Assert.Equal(Shape(await Stock.LedgerAsync(twin.Id, _s.Warehouse)), Shape(await Stock.LedgerAsync(product.Id, _s.Warehouse)));
        Assert.Equal(await Stock.OnHandAsync(twin.Id, _s.Warehouse), await Stock.OnHandAsync(product.Id, _s.Warehouse));
        var read = await _s.ReadAsync(order.Id);
        Assert.Equal((4m, 6m, SalesOrderStatus.Approved), (read.Lines[0].DeliveredQty, read.Lines[0].RemainingQty, read.Status)); // a partial delivery leaves the order open
    }

    // ----- the order -----
    [Theory]
    [InlineData(SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Closed)]
    public async Task Posting_is_409_naming_the_status_when_the_order_is_closed_or_cancelled_and_nothing_is_written(SalesOrderStatus status)
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var ready = await ReadyAsync(order, product.Id, 4m);
        await _s.SetStatusAsync(order, status);

        var problem = (await PostAsync(ready)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, status), problem.Detail);
        Assert.Equal(20m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
        Assert.NotEqual(DocumentStatus.Posted, (await Stock.GetAsync(ready.Id)).Status);
    }

    [Fact]
    public async Task While_the_sales_module_is_off_a_linked_delivery_cannot_be_posted()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var ready = await ReadyAsync(order, product.Id, 4m);
        await _s.TurnOffAsync();

        var problem = (await PostAsync(ready)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.DeliveryCannotPostModuleOff, problem.Detail);
        Assert.Equal(20m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
    }

    // ----- over-delivery, under the lock -----
    [Fact]
    public async Task A_second_delivery_that_would_take_the_line_over_what_remains_is_409_over_delivery_with_the_figures_and_writes_no_ledger_row()
    {
        var (product, order) = await OrderAsync(10m, onHand: 50m);
        var first = await ReadyAsync(order, product.Id, 6m);
        var second = await ReadyAsync(order, product.Id, 5m); // each is within what remained when it was saved: nobody reserved anything
        (await PostAsync(first)).ShouldBeOk();
        var ledgerBefore = (await Stock.LedgerAsync(product.Id, _s.Warehouse)).Count;

        var response = await PostAsync(second);

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("Remaining 4", problem.Detail);
        Assert.Contains("ordered 10, delivered 6", problem.Detail);
        Assert.Contains("over_delivery", response.Body);
        Assert.Equal(ledgerBefore, (await Stock.LedgerAsync(product.Id, _s.Warehouse)).Count);
        Assert.Equal(44m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
        Assert.NotEqual(DocumentStatus.Posted, (await Stock.GetAsync(second.Id)).Status);
        // exactly what remains posts, and that closes the order
        var third = await ReadyAsync(order, product.Id, 4m);
        (await PostAsync(third)).ShouldBeOk();
        Assert.Equal(SalesOrderStatus.Closed, (await _s.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task A_caller_without_so_read_is_refused_in_general_words_that_quote_no_order_figures()
    {
        var (product, order) = await OrderAsync(10m, onHand: 50m);
        var first = await ReadyAsync(order, product.Id, 6m);
        var second = await ReadyAsync(order, product.Id, 5m);
        (await PostAsync(first)).ShouldBeOk();
        var poster = Forged(PermissionCatalog.Codes.Except([Permissions.SoRead, Permissions.SoManage]));

        var problem = (await poster.Documents.PostAsync(second.Id, second.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.DeliveryOverRemaining, problem.Detail);
        Assert.DoesNotContain(order.No, problem.Detail);
        Assert.Equal(44m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
    }

    [Fact]
    public async Task Twenty_five_rounds_of_two_drafts_of_six_posted_at_once_against_a_line_of_ten_let_exactly_one_through_and_the_balance_equals_the_ledger()
    {
        for (var round = 0; round < 25; round++)
        {
            var (product, order) = await OrderAsync(10m, onHand: 12m);
            var a = await ReadyAsync(order, product.Id, 6m);
            var b = await ReadyAsync(order, product.Id, 6m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(a), () => PostAsync(b)]);

            Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
            var loser = results.Single(r => r.Status != HttpStatusCode.OK);
            loser.ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Contains("over_delivery", loser.Body);
            Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
            Assert.Equal(6m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
            Assert.Equal(6m, await Stock.LedgerSumAsync(product.Id, _s.Warehouse));
            Assert.Equal(6m, (await _s.ReadAsync(order.Id)).Lines[0].DeliveredQty);
        }
    }

    [Fact]
    public async Task Linked_deliveries_and_unrelated_issues_of_the_same_products_posted_at_once_all_succeed_with_no_deadlock_left_standing()
    {
        var products = new[] { await _s.NewProductAsync(), await _s.NewProductAsync() };
        foreach (var p in products) await Stock.ReceiveAsync(_s.Warehouse, new Line(p.Id, 500m, 2m));
        var posts = new List<Func<Task<ApiResponse>>>();
        for (var i = 0; i < 6; i++)
        {
            var product = products[i % 2];
            var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 5m, 5m).Build())).Data<SalesOrderDto>();
            var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
            var linked = await ReadyAsync(order, product.Id, 5m);
            posts.Add(() => PostAsync(linked));
            var plain = await Stock.DraftIssueAsync(_s.Clerk, _s.Warehouse, new Line(product.Id, 5m));
            var submitted = await Stock.SubmitAsync(_s.Clerk, plain);
            if (submitted.RequiresApproval) (await Stock.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
            var ready = (await Stock.Admin.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
            posts.Add(() => PostAsync(ready));
        }

        var results = await Concurrently.RunAsync(posts);

        Assert.All(results, r => r.ShouldBeOk()); // a deadlock victim is retried by the unit of work: nothing reaches the caller
        foreach (var p in products)
        {
            Assert.Equal(500m - 3 * 5m * 2, await Stock.OnHandAsync(p.Id, _s.Warehouse));   // three linked and three unlinked issues of 5 per product
            Assert.Equal(470m, await Stock.LedgerSumAsync(p.Id, _s.Warehouse));
        }
    }

    // ----- closing, reversal -----
    [Fact]
    public async Task The_delivery_that_takes_out_the_last_quantity_closes_the_order_as_delivered_in_the_same_transaction_with_an_audit_event_naming_the_poster_and_the_delivery()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var ready = await ReadyAsync(order, product.Id, 10m);

        var posted = (await PostAsync(ready)).Data<DocumentDto>();

        var closed = await _s.ReadAsync(order.Id);
        Assert.Equal((SalesOrderStatus.Closed, SalesOrderCloseKind.Delivered, (string?)null), (closed.Status, closed.CloseKind, closed.CloseReason));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", order.No)));
        var after = await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", order.No));
        Assert.Contains(posted.No, after);
        Assert.Equal(TestUsers.UsernameOf(Roles.Admin), await fixture.Database.ScalarAsync<string>("SELECT Actor FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", order.No)));
        Assert.DoesNotContain(DocumentActions.Deliver, closed.AllowedActions);
    }

    [Fact]
    public async Task A_reversed_delivery_gives_the_quantity_back_and_never_reopens_a_closed_order()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var posted = (await PostAsync(await ReadyAsync(order, product.Id, 10m))).Data<DocumentDto>();
        Assert.Equal(SalesOrderStatus.Closed, (await _s.ReadAsync(order.Id)).Status);

        (await _s.Admin.Documents.ReverseAsync(posted.Id, "Delivered to the wrong customer")).ShouldBeOk();

        var read = await _s.ReadAsync(order.Id);
        Assert.Equal((0m, 10m), (read.Lines[0].DeliveredQty, read.Lines[0].RemainingQty)); // the quantity is back
        Assert.Equal(SalesOrderStatus.Closed, read.Status);                                 // and the order stays closed
        Assert.Equal(20m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
    }

    [Fact]
    public async Task A_reversal_before_the_last_delivery_frees_the_quantity_for_another_delivery()
    {
        var (product, order) = await OrderAsync(10m, onHand: 50m);
        var first = (await PostAsync(await ReadyAsync(order, product.Id, 6m))).Data<DocumentDto>();
        (await _s.Admin.Documents.ReverseAsync(first.Id, "Customer refused the goods")).ShouldBeOk();

        var again = await ReadyAsync(order, product.Id, 10m); // the full ten is available again

        (await PostAsync(again)).ShouldBeOk();
        Assert.Equal(SalesOrderStatus.Closed, (await _s.ReadAsync(order.Id)).Status);
    }

    // ----- stock -----
    [Fact]
    public async Task Not_enough_stock_stops_a_linked_delivery_exactly_as_it_stops_an_unlinked_issue()
    {
        var (product, order) = await OrderAsync(10m, onHand: 3m);
        var twin = await _s.NewProductAsync();
        await Stock.ReceiveAsync(_s.Warehouse, new Line(twin.Id, 3m, 2m));
        var linkedDraft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var plainDraft = await Stock.DraftIssueAsync(_s.Clerk, _s.Warehouse, new Line(twin.Id, 10m));

        var linked = (await _s.Clerk.Documents.SubmitAsync(linkedDraft.Id, linkedDraft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        var plain = (await _s.Clerk.Documents.SubmitAsync(plainDraft.Id, plainDraft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("Insufficient stock", linked.Title + linked.Detail);
        Assert.Contains("Insufficient stock", plain.Title + plain.Detail);
        Assert.Equal(3m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
        Assert.Equal(0m, (await _s.ReadAsync(order.Id)).Lines[0].DeliveredQty);
    }

    [Fact]
    public async Task With_negative_stock_allowed_an_approver_with_a_reason_lets_a_linked_delivery_post_below_zero_and_the_order_counts_it()
    {
        var (product, order) = await OrderAsync(10m, onHand: 3m);
        await using var setting = await SettingsOverride.ApplyAsync(_s.Admin, allowNegativeStock: true);
        var draft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<DocumentDto>();
        Assert.True(submitted.RequiresOverride);

        (await Stock.Supervisor.Documents.ApproveWithReasonAsync(submitted.Id, "Goods arrive tomorrow, customer waits at the counter")).ShouldBeOk();
        (await _s.Supervisor.Documents.PostAsync(submitted.Id)).ShouldBeOk();

        Assert.Equal(-7m, await Stock.OnHandAsync(product.Id, _s.Warehouse));
        var read = await _s.ReadAsync(order.Id);
        Assert.Equal((10m, SalesOrderStatus.Closed), (read.Lines[0].DeliveredQty, read.Status));
    }

    // ----- what is offered -----
    [Fact]
    public async Task Post_is_offered_for_an_approved_linked_delivery_like_for_any_other_issue()
    {
        var (product, order) = await OrderAsync(10m, onHand: 20m);
        var ready = await ReadyAsync(order, product.Id, 4m);

        Assert.Contains(DocumentActions.Post, (await _s.Admin.Documents.GetAsync(ready.Id)).Data<DocumentDto>().AllowedActions);
    }
}
