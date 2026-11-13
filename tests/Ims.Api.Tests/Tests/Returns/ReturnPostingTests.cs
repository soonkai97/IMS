using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Returns;

/// <summary>
/// PL3.17: posting a return, hardened. The races (two returns for the last quantity; the reversal of the original against a return posting, 25 rounds each), a Posted return stops the reversal of its original,
/// ledger and balance parity with an unlinked document, the reversal of a return gives its quantity back, the negative-stock override of a supplier return, the base quantity of partial returns, the cents of
/// customer-return amounts, the return flag of the movements, and the cost rules (D13) with their documented drift. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnPostingTests(ApiFixture fixture) : IAsyncLifetime
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

    private async Task<(ProductDto Product, DocumentDto Receipt, DocumentDto Sale)> SoldAsync(decimal received = 20m, decimal cost = 3m, decimal sold = 8m)
    {
        var product = await _s.NewProductAsync();
        var receipt = await _s.ReceiveAsync(Warehouse, new Line(product.Id, received, cost));
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, sold));
        return (product, receipt, sale);
    }

    private async Task<DocumentDto> DraftReturnAsync(DocumentDto original) => (await _s.Supervisor.Returns.CreateAsync(original.Id)).Data<DocumentDto>();

    private async Task<DocumentDto> ApprovedAsync(DocumentDto draft)
    {
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        return (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
    }

    private async Task<DocumentDto> PostedReturnAsync(DocumentDto original) => (await _s.Admin.Documents.PostAsync((await ApprovedAsync(await DraftReturnAsync(original))).Id)).Data<DocumentDto>();

    private static DocumentSaveRequest WithQty(DocumentDto d, decimal qty) =>
        new(d.WarehouseId, d.PartnerId, d.Purpose, d.Remark, d.Lines.Select(l => new DocumentLineInput(l.ProductId, qty, l.UomId, null, null, ReturnOfLineNo: l.ReturnOfLineNo)).ToList());

    // ----- the original cannot be reversed under its returns -----
    [Fact]
    public async Task An_original_with_a_posted_return_cannot_be_reversed_and_nothing_is_written_until_the_return_is_reversed_first()
    {
        var (product, receipt, sale) = await SoldAsync();
        var back = await PostedReturnAsync(sale);
        var ledgerRows = (await _s.LedgerAsync(product.Id, Warehouse)).Count;
        var audits = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityId = @n", ("@n", sale.No));

        var refused = (await _s.Admin.Documents.ReverseAsync(sale.Id, "entered twice")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.ReverseHasReturns(sale.No), refused.Detail);
        Assert.Equal(ledgerRows, (await _s.LedgerAsync(product.Id, Warehouse)).Count);
        Assert.Equal(audits, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityId = @n", ("@n", sale.No)));
        Assert.Equal(DocumentStatus.Posted, (await _s.GetAsync(sale.Id)).Status);

        (await _s.Admin.Documents.ReverseAsync(back.Id, "returned in error")).ShouldBeOk();                // a Reversed return frees the original
        (await _s.Admin.Documents.ReverseAsync(sale.Id, "entered twice")).ShouldBeOk();
        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task A_receipt_with_a_posted_supplier_return_cannot_be_reversed_either_but_a_receipt_nobody_returned_and_an_issue_for_use_can()
    {
        var (product, receipt, _) = await SoldAsync(20m, 3m, 2m);
        var plain = await _s.ReceiveAsync(Warehouse, new Line(product.Id, 5m, 3m));
        var draft = await DraftReturnAsync(receipt);
        var posted = (await _s.Admin.Documents.PostAsync((await ApprovedAsync((await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, 5m))).Data<DocumentDto>())).Id)).Data<DocumentDto>();

        (await _s.Admin.Documents.ReverseAsync(receipt.Id, "wrong supplier")).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Admin.Documents.ReverseAsync(plain.Id, "wrong supplier")).ShouldBeOk();
        (await _s.Admin.Documents.ReverseAsync(posted.Id, "undo it")).ShouldBeOk();
        (await _s.Admin.Documents.ReverseAsync(receipt.Id, "wrong supplier")).ShouldBeProblem(HttpStatusCode.Conflict); // 18 pieces were sold or are left: the usual stock rule, not the return rule
    }

    [Fact]
    public async Task Reversing_an_original_waits_for_its_lock_and_then_sees_a_return_that_was_posted_while_it_waited()
    {
        var (_, _, sale) = await SoldAsync();
        var draft = await DraftReturnAsync(sale);
        var held = await HeldOrderLock.TakeReturnOriginAsync(fixture, sale.Id);
        Task<ApiResponse>? running = null;
        try
        {
            running = Task.Run(() => _s.Admin.Documents.ReverseAsync(sale.Id, "entered twice"));
            var waited = false;
            for (var i = 0; i < 100 && !running.IsCompleted && !waited; i++)
            {
                waited = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = N'APPLICATION' AND request_status = N'WAIT' AND resource_database_id = DB_ID()") > 0;
                if (!waited) await Task.Delay(50);
            }
            Assert.True(waited, "the reversal did not wait for the original's lock (it must take ret:{id} before it looks for returns)");
            await held.ExecuteAsync("UPDATE StockDocument SET Status = @s WHERE PublicId = @id", ("@s", (int)DocumentStatus.Posted), ("@id", draft.Id)); // a return posts while the reversal waits
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
            if (running is not null) { try { await running.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { /* judged below */ } }
        }

        (await running!).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(DocumentStatus.Posted, (await _s.GetAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task The_reversal_of_an_original_and_the_posting_of_its_return_never_both_win_in_25_rounds_and_stock_is_never_counted_twice()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var (product, _, sale) = await SoldAsync(20m, 3m, 8m);   // 12 left
            var approved = await ApprovedAsync(await DraftReturnAsync(sale));

            var results = await Task.WhenAll(_s.Admin.Documents.ReverseAsync(sale.Id, "race test"), _s.Admin.Documents.PostAsync(approved.Id));

            Assert.Single(results, r => r.Status == HttpStatusCode.OK);                         // exactly one of the two happened
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.True(r.Status == HttpStatusCode.Conflict, r.Body));
            Assert.Equal(20m, await _s.OnHandAsync(product.Id, Warehouse));                    // either way the 8 sold are back once, never twice
            Assert.Equal(20m, await _s.LedgerSumAsync(product.Id, Warehouse));
        }
    }

    // ----- two returns for the last quantity -----
    [Fact]
    public async Task Two_supplier_returns_for_the_last_quantity_never_both_post_in_25_rounds_and_stock_follows_the_ledger()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var product = await _s.NewProductAsync();
            var receipt = await _s.ReceiveAsync(Warehouse, new Line(product.Id, 20m, 3m));
            var a = await ApprovedAsync(await DraftReturnAsync(receipt));
            var b = await ApprovedAsync(await DraftReturnAsync(receipt));

            var results = await Task.WhenAll(_s.Admin.Documents.PostAsync(a.Id), _s.Admin.Documents.PostAsync(b.Id));

            Assert.Single(results, r => r.Status == HttpStatusCode.OK);
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
            Assert.Equal(0m, await _s.OnHandAsync(product.Id, Warehouse));
            Assert.Equal(0m, await _s.LedgerSumAsync(product.Id, Warehouse));
        }
    }

    [Fact]
    public async Task Returns_posting_in_the_same_moment_as_ordinary_documents_of_the_same_product_keep_the_ledger_and_the_balance_equal()
    {
        for (var round = 0; round < 8; round++)
        {
            var (product, _, sale) = await SoldAsync(100m, 3m, 10m);
            var back = await ApprovedAsync(await DraftReturnAsync(sale));
            var issues = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => _s.DraftIssueAsync(_s.Admin, Warehouse, new Line(product.Id, 1m))));
            var submitted = new List<DocumentDto>();
            foreach (var issue in issues) submitted.Add(await _s.SubmitAsync(_s.Admin, issue));

            var results = await Task.WhenAll(submitted.Select(i => _s.Admin.Documents.PostAsync(i.Id)).Append(_s.Admin.Documents.PostAsync(back.Id)));

            Assert.All(results, r => r.ShouldBeOk());
            Assert.Equal(100m - 10m + 10m - 3m, await _s.OnHandAsync(product.Id, Warehouse));
            Assert.Equal(await _s.OnHandAsync(product.Id, Warehouse), await _s.LedgerSumAsync(product.Id, Warehouse));
        }
    }

    // ----- parity, reversal -----
    [Fact]
    public async Task A_customer_return_leaves_the_ledger_and_the_balance_exactly_as_an_ordinary_receipt_of_the_same_lines_at_the_same_cost_does()
    {
        var (product, _, sale) = await SoldAsync();
        var twin = await _s.NewProductAsync();
        await _s.ReceiveAsync(Warehouse, new Line(twin.Id, 20m, 3m));
        await _s.IssueAsync(Warehouse, new Line(twin.Id, 8m));
        var returned = await PostedReturnAsync(sale);
        await _s.ReceiveAsync(Warehouse, new Line(twin.Id, 8m, 3m));

        var a = (await _s.LedgerAsync(product.Id, Warehouse)).OrderBy(r => r.QtyDelta).Select(r => (r.QtyDelta, r.UnitCost)).ToList();
        var b = (await _s.LedgerAsync(twin.Id, Warehouse)).OrderBy(r => r.QtyDelta).Select(r => (r.QtyDelta, r.UnitCost)).ToList();

        Assert.Equal(b, a);
        var (balanceA, balanceB) = (await _s.BalanceAsync(product.Id, Warehouse), await _s.BalanceAsync(twin.Id, Warehouse));
        Assert.Equal((balanceB!.QtyOnHand, balanceB.AvgCost), (balanceA!.QtyOnHand, balanceA.AvgCost));
        Assert.Equal(8m, (await _s.LedgerAsync(product.Id, Warehouse, returned.Id)).Single().QtyDelta);
    }

    [Fact]
    public async Task Reversing_a_return_takes_the_goods_out_again_and_gives_the_returnable_quantity_back()
    {
        var (product, receipt, sale) = await SoldAsync();
        var customerReturn = await PostedReturnAsync(sale);
        Assert.Equal(20m, await _s.OnHandAsync(product.Id, Warehouse));

        (await _s.Admin.Documents.ReverseAsync(customerReturn.Id, "wrong customer")).ShouldBeOk();

        Assert.Equal(12m, await _s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(12m, await _s.LedgerSumAsync(product.Id, Warehouse));
        var line = (await _s.Supervisor.Documents.GetAsync(sale.Id)).Data<DocumentDto>().Lines[0];
        Assert.Equal((0m, 8m), (line.ReturnedQty, line.ReturnableQty));
        var again = await PostedReturnAsync(sale);                                                           // and the whole 8 can be returned once more
        Assert.Equal(DocumentStatus.Posted, again.Status);
        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task A_return_of_a_document_that_was_reversed_or_cancelled_is_refused_and_so_is_posting_one_made_before_the_reversal()
    {
        var (_, _, sale) = await SoldAsync();
        var approved = await ApprovedAsync(await DraftReturnAsync(sale));
        (await _s.Admin.Documents.ReverseAsync(sale.Id, "entered twice")).ShouldBeOk();                       // no return is posted yet: the original may go

        (await _s.Supervisor.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Admin.Documents.PostAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    // ----- negative stock -----
    [Fact]
    public async Task A_supplier_return_that_would_take_more_than_is_on_hand_is_refused_at_submit_and_with_negative_stock_allowed_goes_to_an_approver_who_gives_a_reason()
    {
        var (product, receipt, _) = await SoldAsync(20m, 3m, 15m);                                           // 5 left of the 20 received
        var draft = await DraftReturnAsync(receipt);                                                          // asks for all 20

        (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        await _s.Admin.Settings.UpdateAsync(new SettingsDto(true, false, Limits.DefaultLowStockCooldownHours));
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        Assert.True(submitted.RequiresOverride);
        (await _s.Admin.Documents.PostAsync((await _s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "supplier takes it all back")).Data<DocumentDto>().Id)).ShouldBeOk();
        Assert.Equal(-15m, await _s.OnHandAsync(product.Id, Warehouse));
    }

    // ----- numbers -----
    [Fact]
    public async Task Returns_of_a_unit_with_a_long_factor_never_put_back_more_base_quantity_than_went_out_the_last_return_takes_the_rest()
    {
        var product = await _s.NewProductAsync(customize: b => b.WithConversion(_s.Reference.CartonUnitId, 0.666667m));
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 10m, 1m, _s.Reference.CartonUnitId));
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, 3m, null, _s.Reference.CartonUnitId));  // 3 x 0.666667 = 2.0000 base
        Assert.Equal(2m, sale.Lines[0].BaseQty);

        var returns = new List<DocumentDto>();
        for (var i = 0; i < 3; i++)
        {
            var draft = await DraftReturnAsync(sale);
            var one = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, 1m))).Data<DocumentDto>();
            returns.Add((await _s.Admin.Documents.PostAsync((await ApprovedAsync(one)).Id)).Data<DocumentDto>());
        }

        Assert.Equal([0.6667m, 0.6667m, 0.6666m], returns.Select(r => r.Lines[0].BaseQty));              // the third one is the rest
        Assert.Equal(2m, returns.Sum(r => r.Lines[0].BaseQty));
        Assert.Equal(await _s.OnHandAsync(product.Id, Warehouse), await _s.LedgerSumAsync(product.Id, Warehouse)); // the ledger and the balance agree
        var nothingLeft = (await _s.Supervisor.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("already been returned", nothingLeft.Detail);
    }

    [Fact]
    public async Task Seven_customer_returns_of_a_cost_of_a_third_of_a_ringgit_each_round_to_the_cent_on_their_own_and_the_stock_value_stays_exact()
    {
        var product = await _s.NewProductAsync(customize: b => b.WithConversion(_s.Reference.CartonUnitId, 3m));
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 7m, 1m, _s.Reference.CartonUnitId));            // 21 pieces at 1.00 per carton: 0.3333.. each
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, 7m));
        Assert.Equal(2.33m, sale.TotalAmount);                                                                // round(7 x 0.333333, 2)

        var amounts = new List<decimal?>();
        for (var i = 0; i < 7; i++)
        {
            var draft = await DraftReturnAsync(sale);
            var one = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, WithQty(draft, 1m))).Data<DocumentDto>();
            amounts.Add((await _s.Admin.Documents.PostAsync((await ApprovedAsync(one)).Id)).Data<DocumentDto>().TotalAmount);
        }

        // The rule: every line's amount is its base quantity times the cost per base unit, rounded half away from zero to the cent ON ITS OWN, and a document total is the sum of its lines. Seven returns of
        // one piece are 7 x 0.33 = 2.31, two cents less than the sale's 2.33: the amounts are display figures per document, nothing compensates between documents. The stock itself is exact (quantity x cost per base unit).
        Assert.All(amounts, a => Assert.Equal(0.33m, a));
        Assert.Equal(2.31m, amounts.Sum());
        Assert.Equal(21m, await _s.OnHandAsync(product.Id, Warehouse));
        var balance = (await _s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(0.3333m, Math.Round(balance.AvgCost!.Value, 4));
    }

    [Fact]
    public async Task The_cost_rules_of_a_return_leave_the_moving_average_as_the_plan_says_a_customer_return_at_the_cost_it_left_at_and_a_supplier_return_at_the_current_average()
    {
        // Customer return: 10 at 3.00, sell 5 (cost 3.00), 5 more arrive at 6.00 (average 4.50), the 5 come back at the 3.00 they left at: 15 on hand, value 60.00, average 4.00.
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 10m, 3m));
        var sale = await _s.IssueAsync(Warehouse, new Line(product.Id, 5m));
        await _s.ReceiveAsync(Warehouse, new Line(product.Id, 5m, 6m));
        Assert.Equal(4.5m, (await _s.BalanceAsync(product.Id, Warehouse))!.AvgCost);
        await PostedReturnAsync(sale);
        var after = (await _s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((15m, 4m), (after.QtyOnHand, after.AvgCost));

        // Supplier return: 10 at 3.00 then 10 at 5.00 (average 4.00); returning the first receipt (10 pieces) leaves 10 at 4.00, value 40.00, although what is left cost 50.00 at the supplier: the return goes out
        // at the average (as every issue does), so the value of the stock drifts from the cost of what is physically left. Documented, not corrected (plan D13).
        var other = await _s.NewProductAsync();
        var first = await _s.ReceiveAsync(Warehouse, new Line(other.Id, 10m, 3m));
        await _s.ReceiveAsync(Warehouse, new Line(other.Id, 10m, 5m));
        await PostedReturnAsync(first);
        var left = (await _s.BalanceAsync(other.Id, Warehouse))!;
        Assert.Equal((10m, 4m), (left.QtyOnHand, left.AvgCost));
    }

    // ----- who approves, who sees -----
    [Fact]
    public async Task A_return_is_never_self_approved_or_self_rejected_even_when_the_installation_allows_self_approval()
    {
        var (_, _, sale) = await SoldAsync();
        await _s.Admin.Settings.UpdateAsync(new SettingsDto(false, true, Limits.DefaultLowStockCooldownHours));
        var waitingBefore = (await _s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting; // the shared database holds other tests' submitted documents
        var draft = await DraftReturnAsync(sale);
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();

        Assert.DoesNotContain(DocumentActions.Approve, (await _s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>().AllowedActions);
        (await _s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest); // the self-approval rule of the domain
        (await _s.Supervisor.Documents.RejectAsync(submitted.Id, "not this one")).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(waitingBefore, (await _s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting); // the return is not in its own submitter's inbox: his count did not move
        (await _s.Admin.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();                                          // somebody else approves it

        var ordinary = await _s.DraftIssueAsync(_s.Supervisor, Warehouse, new Line((await _s.NewProductAsync()).Id, 1m));
        Assert.NotNull(ordinary); // (an ordinary document is still self-approvable while the setting is on: not changed by this slice)
    }

    [Fact]
    public async Task A_caller_without_returns_create_is_not_offered_the_actions_of_a_return_he_cannot_review_and_sees_no_link_but_the_same_approval_facts()
    {
        var (_, _, sale) = await SoldAsync();
        var submitted = (await _s.Supervisor.Documents.SubmitAsync((await DraftReturnAsync(sale)).Id)).Data<DocumentDto>();

        var owner = (await _s.Owner.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();                        // the Owner holds no returns.create

        Assert.Null(owner.ReturnOf);                                                                            // the link is not shown
        Assert.Empty(owner.ApprovalTriggers);                                                                   // a return has none: it is the same as for a caller who sees the link
        Assert.DoesNotContain(DocumentActions.Approve, owner.AllowedActions);
        Assert.Equal((true, false), (owner.RequiresApproval, owner.IsLarge));
        (await _s.Owner.Documents.ApproveAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    // ----- reports -----
    [Fact]
    public async Task The_movements_flag_the_rows_of_a_return_so_a_customer_return_can_be_told_from_a_purchase_and_the_export_has_the_column()
    {
        var (product, _, sale) = await SoldAsync();
        var back = await PostedReturnAsync(sale);
        (await _s.Admin.Documents.ReverseAsync(back.Id, "returned in error")).ShouldBeOk();

        var rows = (await _s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>().Items;

        Assert.Equal(2, rows.Count(r => r.IsReturn));                                                           // the posting and the reversal of the return
        Assert.All(rows.Where(r => r.DocNo == back.No), r => Assert.True(r.IsReturn));
        Assert.All(rows.Where(r => r.DocNo != back.No), r => Assert.False(r.IsReturn));
        var export = Xlsx.Read((await _s.Admin.Reports.MovementsExportAsync(product.Id)).Bytes);
        Assert.Equal(ReportHeaders.Movements, export.Headers);
        Assert.Equal(2, export.Rows.Count(r => export.Cell(r, "Return") == ReportHeaders.ReturnFlag));
        Assert.Equal(rows.Count - 2, export.Rows.Count(r => export.Cell(r, "Return") == ""));
    }

    // ----- the database says it too -----
    [Fact]
    public async Task A_document_cannot_be_its_own_return_in_the_database()
    {
        var (_, _, sale) = await SoldAsync();

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => fixture.Database.ExecuteAsync("UPDATE StockDocument SET ReturnOfDocumentId = Id WHERE PublicId = @id", ("@id", sale.Id)));

        Assert.Equal(547, error.Number);
    }
}
