using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Returns;

/// <summary>
/// PL3.17 follow-up (the reviews of PL3.17 and PL3.18): the double post and the post against a cancel, who may post, reverse and create, returns of a delivery made against a sales order and of a receipt made against a
/// purchase order, negative stock on a supplier return, the exact audit and ledger rows of every refusal, the last quantity of a sale raced, partial returns freed by a reversal, the Returns module off, the list
/// canAct of a return and the stable code of an over-return. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnFollowUpTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int Rounds = 25;

    private StockScenario _s = null!;
    private Guid Warehouse => _s.Reference.DefaultWarehouseId;

    public async Task InitializeAsync()
    {
        await Sales.SalesScenario.ResetAsync(fixture);
        foreach (var module in new[] { "purchasing", "sales", "returns" })
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", $"Module.{module}"));
        _s = await StockScenario.CreateAsync(fixture);
    }

    public async Task DisposeAsync()
    {
        await _s.Admin.Settings.UpdateAsync(new SettingsDto(false, false, Limits.DefaultLowStockCooldownHours));
        await Sales.SalesScenario.ResetAsync(fixture);
    }

    private ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private async Task<(ProductDto Product, DocumentDto Receipt, DocumentDto Sale)> SoldAsync(decimal received = 20m, decimal sold = 8m)
    {
        var product = await _s.NewProductAsync();
        var receipt = await _s.ReceiveAsync(Warehouse, new Line(product.Id, received, 3m));
        return (product, receipt, await _s.IssueAsync(Warehouse, new Line(product.Id, sold)));
    }

    private async Task<DocumentDto> DraftAsync(DocumentDto original, ApiClient? by = null) => (await (by ?? _s.Supervisor).Returns.CreateAsync(original.Id)).Data<DocumentDto>();

    private async Task<DocumentDto> ApprovedAsync(DocumentDto draft)
    {
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        return (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
    }

    private static DocumentSaveRequest WithQty(DocumentDto d, decimal qty) =>
        new(d.WarehouseId, d.PartnerId, d.Purpose, d.Remark, d.Lines.Select(l => new DocumentLineInput(l.ProductId, qty, l.UomId, null, null, ReturnOfLineNo: l.ReturnOfLineNo)).ToList());

    private Task<int> AuditsAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityId = @n", ("@n", no));

    private Task<int> LedgerRowsAsync(Guid documentId) => fixture.Database.ScalarAsync<int>(
        "SELECT COUNT(*) FROM StockLedgerEntry WHERE SourceDocId = (SELECT Id FROM StockDocument WHERE PublicId = @id)", ("@id", documentId));

    // ----- A1: the same return twice, a post against a cancel -----
    [Fact]
    public async Task The_same_return_posted_twice_at_once_posts_once_in_25_rounds_and_the_stock_moves_once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (product, _, sale) = await SoldAsync();
            var approved = await ApprovedAsync(await DraftAsync(sale));

            var results = await Task.WhenAll(_s.Admin.Documents.PostAsync(approved.Id, approved.ConcurrencyToken), _s.Admin.Documents.PostAsync(approved.Id, approved.ConcurrencyToken));

            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.True(r.Status == HttpStatusCode.Conflict, r.Body));
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.NotEqual("over_return", r.Problem.Code)); // the loser is told the document changed or is posted, never that it asked for too much
            Assert.Equal(1, await LedgerRowsAsync(approved.Id));
            Assert.Equal(20m, await _s.OnHandAsync(product.Id, Warehouse));
        }
    }

    [Fact]
    public async Task Two_posts_of_the_same_return_that_both_wait_for_the_originals_lock_leave_the_loser_with_document_changed_not_over_return()
    {
        var (product, _, sale) = await SoldAsync();
        var approved = await ApprovedAsync(await DraftAsync(sale));
        var held = await HeldOrderLock.TakeReturnOriginAsync(fixture, sale.Id);
        Task<ApiResponse>[] running = [];
        try
        {
            running = [Task.Run(() => _s.Admin.Documents.PostAsync(approved.Id)), Task.Run(() => _s.Admin.Documents.PostAsync(approved.Id))];
            var waiters = 0;
            for (var i = 0; i < 200 && waiters < 2 && running.All(r => !r.IsCompleted); i++)
            {
                waiters = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()");
                if (waiters < 2) await Task.Delay(50);
            }
            Assert.True(waiters >= 2, $"both posts must have read the Approved return and be waiting for ret:{{id}}, {waiters} waited");
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
            foreach (var r in running) { try { await r.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { /* judged below */ } }
        }

        var results = await Task.WhenAll(running);

        // Both read the return as Approved before the lock. The one that gets the lock second finds the stamp changed (the first one posted it) and starts again on the new state: without that comparison it would
        // judge a return that is already counted against the original and call it an over-return.
        Assert.Single(results, r => r.Status == HttpStatusCode.OK);
        var loser = results.Single(r => r.Status != HttpStatusCode.OK).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal("document_changed", loser.Code);
        Assert.Equal(1, await LedgerRowsAsync(approved.Id));
        Assert.Equal(20m, await _s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_return_posted_while_it_is_cancelled_ends_posted_or_cancelled_never_both_in_25_rounds()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (product, _, sale) = await SoldAsync();
            var approved = await ApprovedAsync(await DraftAsync(sale));

            var results = await Task.WhenAll(_s.Admin.Documents.PostAsync(approved.Id, approved.ConcurrencyToken), _s.Admin.Documents.CancelAsync(approved.Id, "cancelled at the same time", approved.ConcurrencyToken));

            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.True(r.Status == HttpStatusCode.Conflict, r.Body));
            var status = (await _s.GetAsync(approved.Id)).Status;
            Assert.Equal(status == DocumentStatus.Posted ? 1 : 0, await LedgerRowsAsync(approved.Id));
            Assert.Equal(status == DocumentStatus.Posted ? 20m : 12m, await _s.OnHandAsync(product.Id, Warehouse));
        }
    }

    [Fact]
    public async Task A_return_that_changed_while_its_post_waited_for_the_originals_lock_is_refused_in_the_words_of_its_new_state_not_as_a_generic_conflict()
    {
        var (_, _, sale) = await SoldAsync();
        var approved = await ApprovedAsync(await DraftAsync(sale));
        var held = await HeldOrderLock.TakeReturnOriginAsync(fixture, sale.Id);
        Task<ApiResponse>? running = null;
        try
        {
            running = Task.Run(() => _s.Admin.Documents.PostAsync(approved.Id, approved.ConcurrencyToken));
            var waited = false;
            for (var i = 0; i < 100 && !running.IsCompleted && !waited; i++)
            {
                waited = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") > 0;
                if (!waited) await Task.Delay(50);
            }
            Assert.True(waited, "the post did not wait for the original's lock");
            await held.ExecuteAsync("UPDATE StockDocument SET Status = @s, CancelReason = N'cancelled meanwhile' WHERE PublicId = @id", ("@s", (int)DocumentStatus.Cancelled), ("@id", approved.Id)); // somebody cancels it while the post waits
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
            if (running is not null) { try { await running.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { /* judged below */ } }
        }

        var refused = (await running!).ShouldBeProblem(HttpStatusCode.Conflict);

        // The posting starts again on the new state and that state answers in its own words. (The comparison itself is proved by outcome in the two-waiters test above.)
        Assert.NotEqual(Messages.ConcurrencyConflict, refused.Detail);
        Assert.Equal(0, await LedgerRowsAsync(approved.Id));
    }

    // ----- A2: who may, where -----
    [Fact]
    public async Task A_return_of_a_document_in_a_warehouse_outside_the_callers_scope_is_a_404_for_a_scoped_caller_and_posting_and_reversing_ask_for_their_permissions()
    {
        var other = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(other.Id, new Line(product.Id, 10m, 3m));
        var sale = await _s.IssueAsync(other.Id, new Line(product.Id, 4m));
        (await _s.Supervisor.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.NotFound);                 // the Supervisor works in the default warehouse only
        (await _s.Clerk.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.NotFound);

        var (_, _, here) = await SoldAsync();
        var approved = await ApprovedAsync(await DraftAsync(here));
        var withoutPost = Forged(PermissionCatalog.Codes.Except([Permissions.DocumentPost, Permissions.DocumentPostApproved]).ToArray());
        (await withoutPost.Documents.PostAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        var posted = (await _s.Admin.Documents.PostAsync(approved.Id)).Data<DocumentDto>();
        var withoutReverse = Forged(PermissionCatalog.Codes.Except([Permissions.DocumentReverse]).ToArray());
        (await withoutReverse.Documents.ReverseAsync(posted.Id, "reverse it")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _s.Clerk.Documents.ReverseAsync(posted.Id, "reverse it")).ShouldBeProblem(HttpStatusCode.Forbidden);   // the Clerk holds no document.reverse
        Assert.Equal(DocumentStatus.Posted, (await _s.GetAsync(posted.Id)).Status);
    }

    [Fact]
    public async Task While_the_module_is_off_a_posted_return_can_still_be_reversed_and_an_original_under_a_posted_return_still_cannot()
    {
        var (_, _, sale) = await SoldAsync();
        var kept = await PostAsync(await DraftAsync(sale), 3m);
        var (_, _, other) = await SoldAsync();
        var reversible = await PostAsync(await DraftAsync(other), 8m);
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");

        (await _s.Admin.Documents.ReverseAsync(sale.Id, "reverse it")).ShouldBeProblem(HttpStatusCode.Conflict);      // the rule needs no module switch
        (await _s.Admin.Documents.ReverseAsync(reversible.Id, "reverse it")).ShouldBeOk();                           // a reversal never reads one either: the goods go back out
        Assert.Equal(DocumentStatus.Posted, (await _s.GetAsync(kept.Id)).Status);
    }

    private async Task<DocumentDto> PostAsync(DocumentDto draft, decimal qty)
    {
        var saved = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, qty))).Data<DocumentDto>();
        return (await _s.Admin.Documents.PostAsync((await ApprovedAsync(saved)).Id)).Data<DocumentDto>();
    }

    // ----- A3: a delivery against a sales order, a receipt against a purchase order -----
    private async Task<(ProductDto Product, SalesOrderDto Order, DocumentDto Delivery)> DeliveredAsync()
    {
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 20m, 2m));
        var draftOrder = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 10m, 5m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draftOrder.Id, draftOrder.ConcurrencyToken)).Data<SalesOrderDto>();
        var draft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<DocumentDto>();
        if (submitted.RequiresApproval) (await _s.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        var posted = (await _s.Admin.Documents.PostAsync(draft.Id)).Data<DocumentDto>();
        return (product, order, posted);
    }

    private async Task<SalesOrderDto> ReadOrderAsync(Guid id) => (await _s.Admin.SalesOrders.GetAsync(id)).Data<SalesOrderDto>();

    [Fact]
    public async Task A_return_of_a_delivery_leaves_the_orders_delivered_quantity_and_status_alone_and_the_delivery_cannot_be_reversed_under_it()
    {
        var (_, order, posted) = await DeliveredAsync();
        var before = await ReadOrderAsync(order.Id);

        var back = await PostAsync(await DraftAsync(posted), 4m);

        var after = await ReadOrderAsync(order.Id);
        Assert.Equal((before.Status, before.Lines[0].DeliveredQty, before.Lines[0].RemainingQty), (after.Status, after.Lines[0].DeliveredQty, after.Lines[0].RemainingQty)); // a return changes nothing of the order
        var refused = (await _s.Admin.Documents.ReverseAsync(posted.Id, "reverse the delivery")).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ReverseHasReturns(posted.No), refused.Detail);
        (await _s.Admin.Documents.ReverseAsync(back.Id, "reverse the return")).ShouldBeOk();
        (await _s.Admin.Documents.ReverseAsync(posted.Id, "reverse the delivery")).ShouldBeOk();
    }

    [Fact]
    public async Task The_return_of_a_delivery_and_the_reversal_of_that_delivery_never_both_win_in_25_rounds_with_no_deadlock_and_no_server_error()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (product, _, posted) = await DeliveredAsync();
            var approved = await ApprovedAsync(await DraftAsync(posted));

            var results = await Task.WhenAll(_s.Admin.Documents.ReverseAsync(posted.Id, "race of the day"), _s.Admin.Documents.PostAsync(approved.Id));

            Assert.All(results, r => Assert.True((int)r.Status < 500, r.Body));                                   // no deadlock victim, no 40001
            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            var reversed = (await _s.GetAsync(posted.Id)).Status == DocumentStatus.Reversed;
            var returned = (await _s.GetAsync(approved.Id)).Status == DocumentStatus.Posted;
            Assert.True(reversed ^ returned);                                                                     // the original is Reversed XOR the return is Posted
            var loser = results.Single(r => r.Status != HttpStatusCode.OK).ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Contains(reversed ? Messages.ReturnOriginalNotPosted(posted.No, DocumentStatus.Reversed) : Messages.ReverseHasReturns(posted.No), loser.Detail);
            Assert.Equal(reversed ? 20m : 20m, await _s.OnHandAsync(product.Id, Warehouse));
        }
    }

    [Fact]
    public async Task A_supplier_return_of_a_receipt_made_against_a_purchase_order_leaves_the_orders_received_quantity_and_status_alone()
    {
        var product = await _s.NewProductAsync();
        var draftOrder = (await _s.Clerk.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(Warehouse, fixture.Reference.SupplierId).WithLine(product.Id, 10m, 2m).Build())).Data<PurchaseOrderDto>();
        var order = (await _s.Clerk.PurchaseOrders.SubmitAsync(draftOrder.Id, draftOrder.ConcurrencyToken)).Data<PurchaseOrderDto>();
        var draft = (await _s.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<DocumentDto>();
        if (submitted.RequiresApproval) (await _s.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        var receipt = (await _s.Admin.Documents.PostAsync(draft.Id)).Data<DocumentDto>();
        var before = (await _s.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>();

        var back = await PostAsync(await DraftAsync(receipt), 3m);

        var after = (await _s.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>();
        Assert.Equal((before.Status, before.Lines[0].ReceivedQty), (after.Status, after.Lines[0].ReceivedQty));
        var refused = (await _s.Admin.Documents.ReverseAsync(receipt.Id, "reverse the receipt")).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ReverseHasReturns(receipt.No), refused.Detail);
        Assert.Equal(DocumentStatus.Posted, back.Status);
    }

    // ----- A4: negative stock on a supplier return -----
    [Fact]
    public async Task A_poster_without_the_negative_stock_override_is_refused_with_a_400_at_post_for_a_return_that_needs_one_and_stock_that_drops_after_approval_refuses_the_post_with_nothing_written()
    {
        var (product, receipt, _) = await SoldAsync(20m, 15m);                                                       // 5 left of the 20
        await _s.Admin.Settings.UpdateAsync(new SettingsDto(true, false, Limits.DefaultLowStockCooldownHours));
        var submitted = (await _s.Supervisor.Documents.SubmitAsync((await DraftAsync(receipt)).Id)).Data<DocumentDto>();
        Assert.True(submitted.RequiresOverride);

        var noOverride = Forged(PermissionCatalog.Codes.Except([Permissions.StockNegativeOverride]).ToArray());
        (await _s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "take it all back")).ShouldBeOk();
        (await noOverride.Documents.PostAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);             // the override is checked at posting, by the poster: a domain rule, 400 like for any issue
        Assert.Equal(DocumentStatus.Approved, (await _s.GetAsync(submitted.Id)).Status);
        Assert.Equal(0, await LedgerRowsAsync(submitted.Id));
        Assert.Equal(5m, await _s.OnHandAsync(product.Id, Warehouse));

        // The customer side: a receipt can never be short, so a customer return never needs the override whatever the setting says.
        var (_, _, sale) = await SoldAsync();
        var customer = (await _s.Supervisor.Documents.SubmitAsync((await DraftAsync(sale)).Id)).Data<DocumentDto>();
        Assert.False(customer.RequiresOverride);

        // Stock that drops after the approval: the second look at posting refuses and writes nothing, the document stays Approved.
        await _s.Admin.Settings.UpdateAsync(new SettingsDto(false, false, Limits.DefaultLowStockCooldownHours));
        var sameProduct = await _s.NewProductAsync();
        var received = await _s.ReceiveAsync(Warehouse, new Line(sameProduct.Id, 10m, 3m));
        var approved = await ApprovedAsync(await DraftAsync(received));
        await _s.IssueAsync(Warehouse, new Line(sameProduct.Id, 4m));                                                // after the approval, 4 of the 10 leave
        var refused = (await _s.Admin.Documents.PostAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(0, await LedgerRowsAsync(approved.Id));
        Assert.Equal(DocumentStatus.Approved, (await _s.GetAsync(approved.Id)).Status);
        Assert.Equal(6m, await _s.OnHandAsync(sameProduct.Id, Warehouse));
        Assert.NotNull(refused.Detail);
        Assert.NotEqual(0m, await _s.OnHandAsync(product.Id, Warehouse));
    }

    // ----- A5: every refusal writes nothing, a success writes once -----
    [Fact]
    public async Task Every_refusal_of_a_return_writes_no_ledger_row_and_no_audit_row_and_a_post_and_a_reversal_write_exactly_one()
    {
        var (product, _, sale) = await SoldAsync();
        var a = await ApprovedAsync(await DraftAsync(sale));
        var b = await ApprovedAsync(await DraftAsync(sale));
        var auditsBeforePost = await AuditsAsync(a.No);
        var posted = (await _s.Admin.Documents.PostAsync(a.Id)).Data<DocumentDto>();                                  // the whole 8 is back: b is now over
        Assert.Equal(auditsBeforePost + 1, await AuditsAsync(posted.No));
        Assert.Equal(1, await LedgerRowsAsync(posted.Id));
        var beforeB = (Audits: await AuditsAsync(b.No), Rows: await LedgerRowsAsync(b.Id));

        var over = (await _s.Admin.Documents.PostAsync(b.Id)).ShouldBeProblem(HttpStatusCode.Conflict);               // over-return
        Assert.Equal("over_return", over.Code);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, over.Lines!.Value.ValueKind);
        Assert.Equal(1, over.Lines.Value.GetArrayLength());
        var stale = (await _s.Admin.Documents.PostAsync(b.Id, "stale-token-value")).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal((beforeB.Audits, beforeB.Rows), (await AuditsAsync(b.No), await LedgerRowsAsync(b.Id)));
        Assert.NotEqual(over.Detail, stale.Detail);

        (await _s.Admin.Documents.ReverseAsync(sale.Id, "original is not reversible")).ShouldBeProblem(HttpStatusCode.Conflict);
        var saleAudits = await AuditsAsync(sale.No);
        (await _s.Admin.Documents.ReverseAsync(sale.Id, "original is not reversible")).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(saleAudits, await AuditsAsync(sale.No));                                                         // a refusal repeated writes nothing

        // the original is Reversed under a return that was never posted: posting that return finds the original not Posted
        var (_, _, other) = await SoldAsync();
        var waiting = await ApprovedAsync(await DraftAsync(other));
        (await _s.Admin.Documents.ReverseAsync(other.Id, "reverse the sale")).ShouldBeOk();
        var audits = await AuditsAsync(waiting.No);
        (await _s.Admin.Documents.PostAsync(waiting.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal((audits, 0), (await AuditsAsync(waiting.No), await LedgerRowsAsync(waiting.Id)));

        // a post and a reversal of a return write one audit row each
        var count = await AuditsAsync(posted.No);
        (await _s.Admin.Documents.ReverseAsync(posted.Id, "returned in error")).ShouldBeOk();
        Assert.Equal(count + 1, await AuditsAsync(posted.No));
        Assert.Equal(2, await LedgerRowsAsync(posted.Id));                                                            // the posting and its opposite
        Assert.NotNull(product);
    }

    [Fact]
    public async Task A_save_submit_or_post_without_if_match_is_428_and_with_a_stale_one_409_and_nothing_is_written()
    {
        var (_, _, sale) = await SoldAsync();
        var draft = await DraftAsync(sale);
        var audits = await AuditsAsync(draft.No);

        (await _s.Supervisor.Documents.WithoutIfMatchAsync(HttpMethod.Put, $"/api/documents/{draft.Id}", WithQty(draft, 2m))).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        (await _s.Supervisor.Documents.WithoutIfMatchAsync(HttpMethod.Post, $"/api/documents/{draft.Id}/submit")).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, 2m), "stale-token")).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Supervisor.Documents.SubmitAsync(draft.Id, "stale-token")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(audits, await AuditsAsync(draft.No));
        Assert.Equal((DocumentStatus.Draft, 8m), ((await _s.GetAsync(draft.Id)).Status, (await _s.GetAsync(draft.Id)).Lines[0].Qty));
        Assert.Equal(0, await LedgerRowsAsync(draft.Id));
    }

    // ----- races for the last quantity -----
    [Fact]
    public async Task Two_customer_returns_for_the_last_quantity_of_a_sale_post_one_and_refuse_the_other_in_25_rounds()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (product, _, sale) = await SoldAsync();                                                           // 12 on hand, 8 sold
            var a = await ApprovedAsync(await DraftAsync(sale));
            var b = await ApprovedAsync(await DraftAsync(sale));

            var results = await Task.WhenAll(_s.Admin.Documents.PostAsync(a.Id), _s.Admin.Documents.PostAsync(b.Id));

            // A customer return only ever adds stock, so nothing but the over-return check can refuse the second one.
            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            var loser = results.Single(r => r.Status != HttpStatusCode.OK).ShouldBeProblem(HttpStatusCode.Conflict);
            Assert.Equal("over_return", loser.Code);
            Assert.Equal(20m, await _s.OnHandAsync(product.Id, Warehouse));
        }
    }

    [Fact]
    public async Task Two_supplier_returns_for_more_than_a_receipt_brought_in_post_one_and_refuse_the_other_as_an_over_return_in_25_rounds()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var product = await _s.NewProductAsync();
            var receipt = await _s.ReceiveAsync(Warehouse, new Line(product.Id, 20m, 3m));
            await _s.ReceiveAsync(Warehouse, new Line(product.Id, 30m, 3m));                                      // 50 on hand: stock can cover both returns, only the original's 20 cannot
            var a = await ApprovedAsync(await DraftAsync(receipt));
            var b = await ApprovedAsync(await DraftAsync(receipt));

            var results = await Task.WhenAll(_s.Admin.Documents.PostAsync(a.Id), _s.Admin.Documents.PostAsync(b.Id));

            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            Assert.Equal("over_return", results.Single(r => r.Status != HttpStatusCode.OK).ShouldBeProblem(HttpStatusCode.Conflict).Code);
            Assert.Equal(30m, await _s.OnHandAsync(product.Id, Warehouse));
        }
    }

    // ----- the mirror of the lock -----
    [Fact]
    public async Task While_the_originals_return_lock_is_held_both_a_return_post_and_the_reversal_of_the_original_wait_and_afterwards_exactly_one_of_them_wins()
    {
        var (_, _, sale) = await SoldAsync();
        var approved = await ApprovedAsync(await DraftAsync(sale));
        var held = await HeldOrderLock.TakeReturnOriginAsync(fixture, sale.Id);
        Task<ApiResponse>[] running = [];
        try
        {
            running = [Task.Run(() => _s.Admin.Documents.PostAsync(approved.Id)), Task.Run(() => _s.Admin.Documents.ReverseAsync(sale.Id, "reverse the sale"))];
            var waiters = 0;
            for (var i = 0; i < 200 && waiters < 2 && running.All(r => !r.IsCompleted); i++)
            {
                waiters = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()");
                if (waiters < 2) await Task.Delay(50);
            }
            Assert.True(waiters >= 2, $"expected both the return post and the reversal of the original to wait for ret:{{id}}, {waiters} waited (each must take it before reading what it decides on)");
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
            foreach (var r in running) { try { await r.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { /* judged below */ } }
        }

        var results = await Task.WhenAll(running);
        Assert.Single(results, r => r.Status == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
    }

    // ----- partial returns freed by a reversal -----
    [Fact]
    public async Task A_reversed_return_frees_its_quantity_for_a_new_return_and_the_original_shows_returned_and_returnable_all_along()
    {
        var (_, _, sale) = await SoldAsync();                                                                     // 8 sold
        async Task<(decimal Returned, decimal Returnable)> FiguresAsync()
        {
            var line = (await _s.Admin.Documents.GetAsync(sale.Id)).Data<DocumentDto>().Lines[0];
            return (line.ReturnedQty!.Value, line.ReturnableQty!.Value);
        }
        var first = await PostAsync(await DraftAsync(sale), 3m);
        Assert.Equal((3m, 5m), await FiguresAsync());
        var second = await PostAsync(await DraftAsync(sale), 3m);
        Assert.Equal((6m, 2m), await FiguresAsync());
        (await _s.Admin.Documents.ReverseAsync(first.Id, "returned in error")).ShouldBeOk();
        Assert.Equal((3m, 5m), await FiguresAsync());

        var third = await PostAsync(await DraftAsync(sale), 5m);                                                  // the freed 3 and the untouched 2

        Assert.Equal(DocumentStatus.Posted, third.Status);
        Assert.Equal((8m, 0m), await FiguresAsync());
        Assert.Equal(DocumentStatus.Posted, (await _s.GetAsync(second.Id)).Status);
    }

    // ----- fix 7: earlier returns took all of the base quantity -----
    [Fact]
    public async Task A_return_that_finds_no_base_quantity_left_although_entered_quantity_remains_is_refused_in_words_with_nothing_written()
    {
        var product = await _s.NewProductAsync(customize: b => b.WithConversion(_s.Reference.CartonUnitId, 0.00005m));
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 100m, 1m));
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, 3m, null, _s.Reference.CartonUnitId));      // 3 x 0.00005 = 0.00015, held as 0.0002 base
        Assert.Equal(0.0002m, sale.Lines[0].BaseQty);
        var drafts = new List<DocumentDto>();
        for (var i = 0; i < 3; i++)
        {
            var draft = await DraftAsync(sale);
            drafts.Add(await ApprovedAsync((await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, 1m))).Data<DocumentDto>()));
        }
        (await _s.Admin.Documents.PostAsync(drafts[0].Id)).ShouldBeOk();                                          // 0.0001
        (await _s.Admin.Documents.PostAsync(drafts[1].Id)).ShouldBeOk();                                          // 0.0001: all of the base quantity is back, one carton is not

        var refused = (await _s.Admin.Documents.PostAsync(drafts[2].Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("nothing is left to return in the unit of stock", refused.Detail);
        Assert.Equal("return_line_exhausted", refused.Code);
        Assert.Contains("Leave this line out", refused.Detail);
        Assert.Equal(0, await LedgerRowsAsync(drafts[2].Id));
        Assert.Equal(DocumentStatus.Approved, (await _s.GetAsync(drafts[2].Id)).Status);
    }

    // ----- fix 6: the list and self-approval -----
    [Fact]
    public async Task With_self_approval_on_the_list_still_does_not_offer_a_supervisor_the_approval_of_a_return_they_submitted()
    {
        await _s.Admin.Settings.UpdateAsync(new SettingsDto(false, true, Limits.DefaultLowStockCooldownHours));
        var (_, _, sale) = await SoldAsync();
        var submitted = (await _s.Supervisor.Documents.SubmitAsync((await DraftAsync(sale)).Id)).Data<DocumentDto>();

        async Task<bool> CanActAsync(ApiClient who) =>
            (await who.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: true)).Data<PagedResult<DocumentListItem>>().Items.Single(i => i.Id == submitted.Id).CanAct;

        Assert.False(await CanActAsync(_s.Supervisor));                                                           // the detail refuses it too: a return is never self-approved
        Assert.DoesNotContain(DocumentActions.Approve, (await _s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>().AllowedActions);
        Assert.True(await CanActAsync(_s.Admin));
    }

    // ----- fix 8: the module off hides the figures and the permission -----
    [Fact]
    public async Task With_the_returns_module_off_neither_the_document_nor_the_user_info_offers_anything_about_returns_and_with_it_on_both_do()
    {
        var (_, _, sale) = await SoldAsync();
        var onLine = (await _s.Admin.Documents.GetAsync(sale.Id)).Data<DocumentDto>().Lines[0];
        Assert.NotNull(onLine.ReturnedQty);
        Assert.NotNull(onLine.ReturnableQty);
        Assert.Contains(Permissions.ReturnsCreate, (await _s.Admin.Auth.MeAsync()).Data<UserInfo>().Permissions);

        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");

        var offLine = (await _s.Admin.Documents.GetAsync(sale.Id)).Data<DocumentDto>().Lines[0];
        Assert.Null(offLine.ReturnedQty);
        Assert.Null(offLine.ReturnableQty);
        var me = (await _s.Admin.Auth.MeAsync()).Data<UserInfo>();
        Assert.DoesNotContain(Permissions.ReturnsCreate, me.Permissions);
        Assert.DoesNotContain("returns", me.Modules);
    }

    // ----- review of the follow-up: sign-in and refresh list returns.create while Returns is on, and not while it is off -----
    [Fact]
    public async Task Sign_in_and_refresh_list_returns_create_while_the_module_is_on_and_leave_it_out_while_it_is_off()
    {
        async Task<(string[] SignIn, string[] Refresh)> ListsAsync()
        {
            var client = fixture.Anonymous();
            var signedIn = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>();
            var refreshed = (await client.Auth.RefreshAsync(signedIn.RefreshToken!)).Data<AuthResult>();
            return (signedIn.User.Permissions.ToArray(), refreshed.User.Permissions.ToArray());
        }

        var on = await ListsAsync();
        Assert.Contains(Permissions.ReturnsCreate, on.SignIn);
        Assert.Contains(Permissions.ReturnsCreate, on.Refresh);

        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");

        var off = await ListsAsync();
        Assert.DoesNotContain(Permissions.ReturnsCreate, off.SignIn);
        Assert.DoesNotContain(Permissions.ReturnsCreate, off.Refresh);
    }

    // ----- review of the follow-up: creating a return leaves out a line whose base quantity is all back -----
    [Fact]
    public async Task Creating_a_return_leaves_out_a_line_that_has_quantity_left_but_no_base_quantity()
    {
        var product = await _s.NewProductAsync(customize: b => b.WithConversion(_s.Reference.CartonUnitId, 0.00005m));
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 100m, 1m));
        var plain = await _s.NewProductAsync();
        await _s.ReceiveAsync(Warehouse, new Line(plain.Id, 10m, 1m));
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, 3m, null, _s.Reference.CartonUnitId), new Line(plain.Id, 2m));
        var drafts = new List<DocumentDto>();
        for (var i = 0; i < 2; i++)
        {
            var draft = await DraftAsync(sale);
            var one = new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, draft.Purpose, draft.Remark,
                draft.Lines.Where(l => l.ReturnOfLineNo == 1).Select(l => new DocumentLineInput(l.ProductId, 1m, l.UomId, null, null, ReturnOfLineNo: 1)).ToList());
            drafts.Add(await ApprovedAsync((await _s.Supervisor.Documents.UpdateAsync(draft.Id, one)).Data<DocumentDto>()));
        }
        foreach (var d in drafts) (await _s.Admin.Documents.PostAsync(d.Id)).ShouldBeOk();                       // 2 of 3 cartons are back and they took all of the 0.0002 base

        var next = (await _s.Supervisor.Returns.CreateAsync(sale.Id)).Data<DocumentDto>();

        Assert.Equal([2], next.Lines.Select(l => l.ReturnOfLineNo!.Value));                                          // the carton line is left out, the plain line is offered
    }
}
