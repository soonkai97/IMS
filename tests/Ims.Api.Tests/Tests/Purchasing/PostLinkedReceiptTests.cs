using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.5: posting a receipt that was created from an order. The engine, the ledger and the average are the unlinked receipt's; around them the order's lock is taken first, the order must be Approved
/// and the module on, the quantities are checked again under the lock (the locked check; the one at save time is advisory), and the order closes itself when the last quantity is in.
/// </summary>
[Collection(ApiCollection.Name)]
public class PostLinkedReceiptTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private ProductDto _a = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        _a = await _p.NewProductAsync();
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    /// <summary>A Draft receipt of the order (made by the Clerk) with line 1 set to <paramref name="qty"/>, submitted by the Clerk; not posted.</summary>
    private async Task<DocumentDto> SubmittedAsync(PurchaseOrderDto order, decimal qty, ProductDto? product = null, int lineNo = 1)
    {
        var receipt = await _p.ReceiptFromAsync(order);
        var saved = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor((product ?? _a).Id, qty, lineNo)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        return await _p.Stock.SubmitAsync(_p.Clerk, saved);
    }

    private async Task<DocumentDto> PostedAsync(PurchaseOrderDto order, decimal qty, ProductDto? product = null, int lineNo = 1) =>
        await _p.Stock.CompleteAsync(await SubmittedAsync(order, qty, product, lineNo));

    private Task<ApiResponse> PostAsync(DocumentDto submitted) => _p.Admin.Documents.PostAsync(submitted.Id);

    [Fact]
    public async Task A_linked_receipt_posts_the_same_ledger_rows_and_average_as_an_unlinked_receipt_of_the_same_lines_and_the_order_shows_what_came_in()
    {
        var (linkedProduct, plainProduct) = (await _p.NewProductAsync(), await _p.NewProductAsync());
        var order = await _p.ApprovedAsync((linkedProduct.Id, 10m, 3.25m, null));

        await PostedAsync(order, 10m, linkedProduct);
        await _p.Stock.ReceiveAsync(order.WarehouseId, new Line(plainProduct.Id, 10m, 3.25m));

        var (linkedRows, plainRows) = (await _p.Stock.LedgerAsync(linkedProduct.Id, order.WarehouseId), await _p.Stock.LedgerAsync(plainProduct.Id, order.WarehouseId));
        Assert.Equal(plainRows.Select(r => (r.QtyDelta, r.UnitCost, r.Reason, r.BalanceAfter)), linkedRows.Select(r => (r.QtyDelta, r.UnitCost, r.Reason, r.BalanceAfter)));
        var (linkedBalance, plainBalance) = (await _p.Stock.BalanceAsync(linkedProduct.Id, order.WarehouseId), await _p.Stock.BalanceAsync(plainProduct.Id, order.WarehouseId));
        Assert.Equal((plainBalance!.QtyOnHand, plainBalance.AvgCost), (linkedBalance!.QtyOnHand, linkedBalance.AvgCost));
        Assert.Equal((10m, 0m), ((await _p.ReadAsync(order.Id)).Lines.Single().ReceivedQty, (await _p.ReadAsync(order.Id)).Lines.Single().RemainingQty));
    }

    [Fact]
    public async Task A_partial_receipt_leaves_the_order_approved_and_the_last_quantity_closes_it_with_an_audit_event_naming_the_receipt()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));

        await PostedAsync(order, 6m);
        var partial = await _p.ReadAsync(order.Id);
        Assert.Equal((PurchaseOrderStatus.Approved, 6m, 4m, null), (partial.Status, partial.Lines.Single().ReceivedQty, partial.Lines.Single().RemainingQty, partial.CloseKind));

        var last = await PostedAsync(order, 4m);

        var closed = await _p.ReadAsync(order.Id);
        Assert.Equal((PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Received, 10m, 0m), (closed.Status, closed.CloseKind, closed.Lines.Single().ReceivedQty, closed.Lines.Single().RemainingQty));
        Assert.NotNull(closed.ClosedAt);
        Assert.Empty(closed.AllowedActions);
        var audit = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'purchase-order.closed' AND EntityId = @no AND Actor = @who AND After LIKE @receipt",
            ("@no", order.No), ("@who", TestUsers.UsernameOf(Roles.Admin)), ("@receipt", $"%{last.No}%"));
        Assert.Equal(1, audit); // the poster (the Administrator posts in these tests) and the receipt that did it
    }

    [Fact]
    public async Task A_receipt_over_what_remains_is_refused_at_posting_with_the_figures_and_nothing_is_written_and_a_receipt_of_the_remainder_posts()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var first = await SubmittedAsync(order, 6m);
        var second = await SubmittedAsync(order, 6m); // a draft reserves nothing: both were accepted
        await _p.Stock.CompleteAsync(first);
        var ledgerBefore = (await _p.Stock.LedgerAsync(_a.Id, order.WarehouseId)).Count;

        var refused = await PostAsync(second);

        var problem = refused.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("Remaining 4", problem.Detail);
        Assert.Contains("ordered 10", problem.Detail);
        Assert.Contains("received 6", problem.Detail);
        Assert.Contains(_a.Sku, problem.Detail);
        using var body = refused.Json();
        Assert.Equal("over_receipt", body.RootElement.GetProperty("code").GetString());
        var line = body.RootElement.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal((1, _a.Sku, 10m, 6m, 4m, 6m), (line.GetProperty("lineNo").GetInt32(), line.GetProperty("sku").GetString(), line.GetProperty("ordered").GetDecimal(),
            line.GetProperty("received").GetDecimal(), line.GetProperty("remaining").GetDecimal(), line.GetProperty("requested").GetDecimal()));
        Assert.DoesNotContain("2.00", refused.Body); // quantities only: no price in the answer
        Assert.Equal(ledgerBefore, (await _p.Stock.LedgerAsync(_a.Id, order.WarehouseId)).Count);
        Assert.Equal(DocumentStatus.Submitted, (await _p.Stock.GetAsync(second.Id)).Status);

        // the remainder of 4 posts, and the order closes
        var smaller = (await _p.Clerk.Documents.RecallAsync(second.Id, (await _p.Stock.GetAsync(second.Id)).ConcurrencyToken)).Data<DocumentDto>();
        var saved = (await _p.Clerk.Documents.UpdateAsync(smaller.Id, Save(smaller, LineFor(_a.Id, 4m, 1)), smaller.ConcurrencyToken)).Data<DocumentDto>();
        await _p.Stock.CompleteAsync(await _p.Stock.SubmitAsync(_p.Clerk, saved));
        Assert.Equal(PurchaseOrderStatus.Closed, (await _p.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Two_receipts_posting_the_last_quantity_at_the_same_moment_give_exactly_one_winner_each_round_and_the_balance_equals_the_ledger()
    {
        for (var round = 0; round < 25; round++)
        {
            var product = await _p.NewProductAsync();
            var order = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
            var (one, two) = (await SubmittedAsync(order, 6m, product), await SubmittedAsync(order, 6m, product));

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(one), () => PostAsync(two)]);

            Assert.True(results.Count(r => r.IsSuccess) == 1 && results.Count(r => r.StatusCode == 409) == 1, $"round {round}: {string.Join(", ", results.Select(r => r.StatusCode))}");
            Assert.Equal(6m, await _p.Stock.OnHandAsync(product.Id, order.WarehouseId));
            Assert.Equal(6m, await _p.Stock.LedgerSumAsync(product.Id, order.WarehouseId));
            Assert.Equal(6m, (await _p.ReadAsync(order.Id)).Lines.Single().ReceivedQty);
        }
    }

    [Fact]
    public async Task Parallel_posts_of_linked_receipts_and_unrelated_issues_of_the_same_products_deadlock_nowhere_and_keep_every_balance_equal_to_its_ledger()
    {
        var (p, q) = (await _p.NewProductAsync(), await _p.NewProductAsync());
        await _p.Stock.ReceiveAsync(_p.Warehouse, new Line(p.Id, 1000m, 1m), new Line(q.Id, 1000m, 1m));
        for (var round = 0; round < 12; round++)
        {
            var order = await _p.ApprovedAsync((p.Id, 10m, 2m, null), (q.Id, 10m, 2m, null));
            var one = await SubmittedAsync(order, 5m, p, 1);
            var two = await SubmittedAsync(order, 5m, q, 2);
            var issue = await _p.Stock.SubmitAsync(_p.Admin, await _p.Stock.DraftIssueAsync(_p.Admin, _p.Warehouse, new Line(p.Id, 1m), new Line(q.Id, 1m)));

            var results = await Concurrently.RunAsync<ApiResponse>([() => PostAsync(one), () => PostAsync(two), () => PostAsync(issue), () => _p.Admin.PurchaseOrders.CreateReceiptAsync(order.Id)]);

            Assert.All(results, r => Assert.True(r.StatusCode is 200 or 409, $"round {round}: {r.StatusCode} {r.Body}")); // no 500 from a deadlock
            Assert.Equal(await _p.Stock.LedgerSumAsync(p.Id, _p.Warehouse), await _p.Stock.OnHandAsync(p.Id, _p.Warehouse));
            Assert.Equal(await _p.Stock.LedgerSumAsync(q.Id, _p.Warehouse), await _p.Stock.OnHandAsync(q.Id, _p.Warehouse));
        }
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task A_receipt_of_an_order_that_is_no_longer_approved_cannot_post_and_nothing_is_written(PurchaseOrderStatus status)
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var submitted = await SubmittedAsync(order, 3m);
        await _p.SetStatusAsync(order, status); // closed short or cancelled while the receipt waited

        var problem = (await PostAsync(submitted)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.PurchaseOrderNotReceivable(order.No, status), problem.Detail);
        Assert.Empty(await _p.Stock.LedgerAsync(_a.Id, order.WarehouseId));
        Assert.Equal(DocumentStatus.Submitted, (await _p.Stock.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task While_the_module_is_off_a_linked_receipt_cannot_post_and_an_ordinary_receipt_still_can()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var submitted = await SubmittedAsync(order, 3m);
        var plain = await _p.Stock.SubmitAsync(_p.Admin, await _p.Stock.DraftReceiptAsync(_p.Admin, _p.Warehouse, new Line(_a.Id, 1m, 1m)));
        await _p.TurnOffAsync();

        Assert.Equal(Messages.ReceiptCannotPostModuleOff, (await PostAsync(submitted)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        (await PostAsync(plain)).ShouldBeOk();
    }

    [Fact]
    public async Task Reversing_a_posted_linked_receipt_gives_the_quantity_back_and_never_reopens_a_closed_order()
    {
        var open = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var partial = await PostedAsync(open, 6m);
        (await _p.Admin.Documents.ReverseAsync(partial.Id, "Entered twice")).ShouldBeOk();
        Assert.Equal((PurchaseOrderStatus.Approved, 0m, 10m), ((await _p.ReadAsync(open.Id)).Status, (await _p.ReadAsync(open.Id)).Lines.Single().ReceivedQty, (await _p.ReadAsync(open.Id)).Lines.Single().RemainingQty));

        var other = await _p.NewProductAsync();
        var whole = await _p.ApprovedAsync((other.Id, 5m, 2m, null));
        var full = await PostedAsync(whole, 5m, other);
        Assert.Equal(PurchaseOrderStatus.Closed, (await _p.ReadAsync(whole.Id)).Status);

        (await _p.Admin.Documents.ReverseAsync(full.Id, "Entered twice")).ShouldBeOk();

        var after = await _p.ReadAsync(whole.Id);
        Assert.Equal((PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Received, 5m), (after.Status, after.CloseKind, after.Lines.Single().RemainingQty)); // closed stays closed; a shortfall comes back through a new order
        (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(whole.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submitting_a_linked_receipt_checks_what_remains_again_and_says_so_in_words()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var waiting = await _p.ReceiptFromAsync(order); // prefilled with all 10
        await PostedAsync(order, 6m); // meanwhile 6 came in

        var problem = (await _p.Clerk.Documents.SubmitAsync(waiting.Id, waiting.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Remaining 4", Assert.Single(problem.ErrorsFor("lines[0].qty")));
        Assert.Equal(DocumentStatus.Draft, (await _p.Stock.GetAsync(waiting.Id)).Status);
    }

    [Fact]
    public async Task Posting_needs_the_post_right_and_a_receipt_in_a_warehouse_the_caller_can_see_and_a_receipt_that_is_not_linked_is_unchanged()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var submitted = await SubmittedAsync(order, 3m);
        var cannot = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [Permissions.StockRead, Permissions.DocumentCreate]));
        (await cannot.Documents.PostAsync(submitted.Id)).ShouldBe(HttpStatusCode.Forbidden);

        var elsewhere = await _p.NewWarehouseAsync();
        var far = (await _p.Admin.PurchaseOrders.CreateAsync(_p.Request(elsewhere.Id, _a.Id).Build())).Data<PurchaseOrderDto>();
        far = (await _p.Admin.PurchaseOrders.SubmitAsync(far.Id, far.ConcurrencyToken)).Data<PurchaseOrderDto>();
        var farReceipt = await _p.Stock.SubmitAsync(_p.Admin, await _p.ReceiptFromAsync(far, _p.Admin));
        (await _p.Supervisor.Documents.PostAsync(farReceipt.Id)).ShouldBe(HttpStatusCode.NotFound); // the Supervisor works in the default warehouse only

        (await PostAsync(submitted)).ShouldBeOk();
    }
}
