using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Returns;

/// <summary>
/// PL3.16: the Draft return of a Posted document (POST /api/returns) and what makes it a return: the partner, the purpose and the link are the original's, the lines name the original lines by position, a customer
/// return takes the cost the goods left at, a supplier return the average, every return needs one standard-tier approval, and the returned quantities show on the original. Real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnsTests(ApiFixture fixture) : IAsyncLifetime
{
    private StockScenario _s = null!;

    public async Task InitializeAsync()
    {
        await Sales.SalesScenario.ResetAsync(fixture); // every module off, sales rules back
        foreach (var module in new[] { "purchasing", "sales", "returns" })
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", $"Module.{module}"));
        _s = await StockScenario.CreateAsync(fixture);
    }

    public Task DisposeAsync() => Sales.SalesScenario.ResetAsync(fixture);

    /// <summary>A product with 20 pieces received at 3.00, and a posted sale of 8 of them (cost 3.00).</summary>
    private async Task<(Guid Warehouse, ProductDto Product, DocumentDto Receipt, DocumentDto Sale)> SoldAsync()
    {
        var warehouse = _s.Reference.DefaultWarehouseId; // the warehouse the Supervisor, the Clerk and the staff work in
        var product = await _s.NewProductAsync();
        var receipt = await _s.ReceiveAsync(warehouse, new Line(product.Id, 20m, 3m));
        var sale = await _s.IssueAsync(warehouse, new Line(product.Id, 8m));
        return (warehouse, product, receipt, sale);
    }

    private static DocumentSaveRequest Resave(DocumentDto d, Func<DocumentLineDto, int, DocumentLineInput>? line = null, IssuePurpose? purpose = null, Guid? partner = null) =>
        new(d.WarehouseId, partner ?? d.PartnerId, purpose ?? d.Purpose, d.Remark,
            d.Lines.Select((l, i) => line?.Invoke(l, i) ?? new DocumentLineInput(l.ProductId, l.Qty, l.UomId, null, l.Remark, ReturnOfLineNo: l.ReturnOfLineNo)).ToList());

    private async Task<DocumentDto> ReturnOfAsync(DocumentDto original, ApiClient? by = null) => (await (by ?? _s.Supervisor).Returns.CreateAsync(original.Id)).Data<DocumentDto>();

    // ----- creating -----
    [Fact]
    public async Task A_customer_return_is_a_draft_receipt_of_the_sale_with_the_customer_the_quantity_that_can_come_back_and_the_cost_the_goods_left_at()
    {
        var (warehouse, product, _, sale) = await SoldAsync();

        var response = await _s.Supervisor.Returns.CreateAsync(sale.Id);
        var draft = response.Data<DocumentDto>();

        Assert.Equal((DocumentType.Receipt, DocumentStatus.Draft, null), (draft.Type, draft.Status, draft.Purpose));
        Assert.Equal((warehouse, _s.Reference.CustomerId), (draft.WarehouseId, draft.PartnerId!.Value));
        Assert.Equal((sale.Id, sale.No), (draft.ReturnOf!.Id, draft.ReturnOf.No));
        Assert.StartsWith("RCV-", draft.No);
        var line = Assert.Single(draft.Lines);
        Assert.Equal((product.Id, 8m, 1), (line.ProductId, line.Qty, line.ReturnOfLineNo));
        Assert.Equal(3m, line.BaseUnitCost);      // the cost the goods left at, not typed by anybody
        Assert.Equal(24m, line.Amount);
        Assert.False(line.CostFromAverage);
        Assert.Equal($"\"{draft.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'document.created' AND EntityId = @n", ("@n", draft.No)));
    }

    [Fact]
    public async Task A_supplier_return_is_a_draft_issue_with_purpose_Return_for_the_supplier_valued_at_the_average()
    {
        var (warehouse, product, receipt, _) = await SoldAsync();

        var draft = await ReturnOfAsync(receipt);

        Assert.Equal((DocumentType.Issue, IssuePurpose.Return, _s.Reference.SupplierId), (draft.Type, draft.Purpose, draft.PartnerId!.Value));
        Assert.StartsWith("ISS-", draft.No);
        var line = Assert.Single(draft.Lines);
        Assert.Equal((product.Id, 20m, 1, true), (line.ProductId, line.Qty, line.ReturnOfLineNo, line.CostFromAverage));
        Assert.Equal(warehouse, draft.WarehouseId);
    }

    [Fact]
    public async Task Only_a_posted_sale_or_receipt_that_is_not_a_return_and_has_something_left_can_be_returned()
    {
        var (warehouse, product, receipt, sale) = await SoldAsync();
        var openIssue = await _s.DraftIssueAsync(_s.Admin, warehouse, new Line(product.Id, 1m));
        var use = await _s.IssueAsync(warehouse, new Line(product.Id, 1m)); // purpose Sale by default: make one for Use below
        var useDraft = (await _s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(warehouse).WithPartner(_s.Reference.CustomerId).WithPurpose(IssuePurpose.Use).WithLines(new Line(product.Id, 1m)).Build())).Data<DocumentDto>();
        var usePosted = await _s.CompleteAsync(await _s.SubmitAsync(_s.Admin, useDraft));

        (await _s.Admin.Returns.CreateAsync(openIssue.Id)).ShouldBeProblem(HttpStatusCode.Conflict);                  // a draft
        (await _s.Admin.Returns.CreateAsync(usePosted.Id)).ShouldBeProblem(HttpStatusCode.Conflict);                  // not a sale
        (await _s.Admin.Returns.CreateAsync(Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _s.Admin.Documents.ReverseAsync(use.Id, "reversed for the test")).ShouldBeOk();
        (await _s.Admin.Returns.CreateAsync(use.Id)).ShouldBeProblem(HttpStatusCode.Conflict);                        // reversed
        Assert.NotNull(receipt);
        Assert.NotNull(sale);
    }

    [Fact]
    public async Task A_return_cannot_be_returned_and_a_document_with_everything_returned_has_nothing_left()
    {
        var (_, _, _, sale) = await SoldAsync();
        var draft = await ReturnOfAsync(sale);
        var posted = await PostAsync(draft);

        (await _s.Admin.Returns.CreateAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.Conflict);                     // a return is not returned
        var again = (await _s.Admin.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ReturnNothingToReturn(sale.No), again.Detail);
    }

    // ----- who may -----
    [Fact]
    public async Task Supervisor_clerk_staff_and_admin_may_create_a_return_the_owner_and_a_token_without_returns_create_may_not()
    {
        var (_, _, _, sale) = await SoldAsync();
        ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

        (await _s.Owner.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Forged(Permissions.DocumentCreate, Permissions.StockRead).Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Forged(Permissions.ReturnsCreate).Returns.CreateAsync(sale.Id)).ShouldBeOk();
        foreach (var who in new[] { _s.Supervisor, _s.Clerk, _s.Staff, _s.Admin })
            Assert.Equal(HttpStatusCode.OK, (await who.Returns.CreateAsync(sale.Id)).Status);
    }

    [Fact]
    public async Task While_the_module_is_off_creating_a_return_is_404_and_without_a_token_401()
    {
        var (_, _, _, sale) = await SoldAsync();
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");

        (await _s.Admin.Returns.CreateAsync(sale.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal(401, (await fixture.Anonymous().Returns.CreateAsync(sale.Id)).StatusCode);
    }

    // ----- saving -----
    [Fact]
    public async Task A_return_draft_can_ask_for_less_but_not_for_more_than_can_come_back_nor_change_its_partner_purpose_product_or_unit()
    {
        var (_, product, receipt, sale) = await SoldAsync();
        var draft = await ReturnOfAsync(sale);
        var other = await _s.NewProductAsync();

        var less = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 5m, l.UomId, 99m, null, ReturnOfLineNo: 1)))).Data<DocumentDto>();
        Assert.Equal((5m, 3m, 15m), (less.Lines[0].Qty, less.Lines[0].BaseUnitCost, less.Lines[0].Amount)); // a typed cost of 99 is discarded: the goods come back at 3.00

        var over = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 8.0001m, l.UomId, null, null, ReturnOfLineNo: 1)), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(over.ErrorsFor("lines[0].qty"));
        var split = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, null, null,
            [new DocumentLineInput(product.Id, 5m, null, null, null, ReturnOfLineNo: 1), new DocumentLineInput(product.Id, 4m, null, null, null, ReturnOfLineNo: 1)]), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(split.ErrorsFor("lines[0].qty")); // splitting does not get past the sum

        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, partner: _s.Reference.SupplierId), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(other.Id, 1m, null, null, null, ReturnOfLineNo: 1)), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 1m, _s.Reference.CartonUnitId, null, null, ReturnOfLineNo: 1)), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 1m, null, null, null, ReturnOfLineNo: 2)), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 1m, null, null, null)), less.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest); // every line must name one
        var supplier = await ReturnOfAsync(receipt);
        (await _s.Supervisor.Documents.UpdateAsync(supplier.Id, Resave(supplier, purpose: IssuePurpose.Use))).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_purpose_Return_cannot_be_chosen_and_a_document_that_is_not_a_return_takes_no_return_line_number()
    {
        var warehouse = _s.Reference.DefaultWarehouseId; // the warehouse the Supervisor, the Clerk and the staff work in
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(warehouse, new Line(product.Id, 5m, 1m));

        (await _s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(warehouse).WithPartner(_s.Reference.CustomerId).WithPurpose(IssuePurpose.Return).WithLines(new Line(product.Id, 1m)).Build()))
            .ShouldBeProblem(HttpStatusCode.BadRequest);
        var request = new DocumentSaveRequest(warehouse, _s.Reference.CustomerId, IssuePurpose.Sale, null, [new DocumentLineInput(product.Id, 1m, null, null, null, ReturnOfLineNo: 1)]);
        var plain = (await _s.Admin.Documents.CreateIssueAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains(Messages.ReturnNotLinked, plain.ErrorsFor("lines[0].returnOfLineNo"));

        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");
        var off = (await _s.Admin.Documents.CreateIssueAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(off.ErrorsFor("lines[0].returnOfLineNo")); // not available while the module is off
    }

    // ----- approval -----
    [Fact]
    public async Task A_return_is_always_held_for_one_standard_tier_approval_whatever_it_is_worth_and_the_owner_is_not_its_reviewer()
    {
        var (_, _, _, sale) = await SoldAsync();
        var draft = await ReturnOfAsync(sale, _s.Clerk);

        var submitted = (await _s.Clerk.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal((DocumentStatus.Submitted, true, false), (submitted.Status, submitted.RequiresApproval, submitted.IsLarge));
        Assert.Empty(submitted.ApprovalTriggers);
        Assert.DoesNotContain(DocumentActions.Approve, (await _s.Owner.Documents.GetAsync(submitted.Id)).Data<DocumentDto>().AllowedActions);
        Assert.Contains(DocumentActions.Approve, (await _s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>().AllowedActions);
        (await _s.Owner.Documents.ApproveAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Approved, (await _s.Supervisor.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>().Status);
    }

    [Fact]
    public async Task A_huge_return_still_goes_to_the_standard_tier_not_to_the_owner()
    {
        var warehouse = _s.Reference.DefaultWarehouseId; // the warehouse the Supervisor, the Clerk and the staff work in
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(warehouse, new Line(product.Id, 1000m, 5000m));   // 5,000,000.00 of stock
        var sale = await _s.IssueAsync(warehouse, new Line(product.Id, 900m));  // a sale far above any approval threshold
        var draft = await ReturnOfAsync(sale);

        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal((true, false), (submitted.RequiresApproval, submitted.IsLarge));
        Assert.Equal(DocumentStatus.Approved, (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>().Status);
    }

    // ----- posting, quantities, reversal -----
    private async Task<DocumentDto> PostAsync(DocumentDto draft)
    {
        var submitted = (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        var approved = (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
        return (await _s.Admin.Documents.PostAsync(approved.Id)).Data<DocumentDto>();
    }

    [Fact]
    public async Task A_customer_return_puts_the_goods_back_at_the_cost_they_left_at_and_a_supplier_return_takes_them_out_at_the_average()
    {
        var (warehouse, product, receipt, sale) = await SoldAsync();
        await _s.ReceiveAsync(warehouse, new Line(product.Id, 10m, 6m)); // the average moves on: 12 pieces at 3.00 and 10 at 6.00
        var average = (await _s.BalanceAsync(product.Id, warehouse))!.AvgCost!.Value;

        var customerReturn = await PostAsync(await ReturnOfAsync(sale));

        var ledger = (await _s.LedgerAsync(product.Id, warehouse, customerReturn.Id)).Single();
        Assert.Equal((8m, 3m), (ledger.QtyDelta, ledger.UnitCost));
        Assert.Equal(30m, await _s.OnHandAsync(product.Id, warehouse));
        Assert.NotEqual(average, (await _s.BalanceAsync(product.Id, warehouse))!.AvgCost); // the moving average moved like any receipt

        var supplierReturn = await PostAsync(await ReturnOfAsync(receipt));
        var out1 = (await _s.LedgerAsync(product.Id, warehouse, supplierReturn.Id)).Single();
        Assert.Equal(-20m, out1.QtyDelta);
        Assert.Equal(10m, await _s.OnHandAsync(product.Id, warehouse));
    }

    [Fact]
    public async Task The_original_shows_what_came_back_and_what_can_still_come_back_only_to_a_caller_who_may_create_returns_and_a_reversed_return_counts_as_nothing()
    {
        var (_, _, _, sale) = await SoldAsync();
        var draft = await ReturnOfAsync(sale);
        var partial = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, (l, _) => new DocumentLineInput(l.ProductId, 3m, null, null, null, ReturnOfLineNo: 1)))).Data<DocumentDto>();
        var posted = await PostAsync(partial);

        var read = (await _s.Supervisor.Documents.GetAsync(sale.Id)).Data<DocumentDto>();
        Assert.Equal((3m, 5m), (read.Lines[0].ReturnedQty, read.Lines[0].ReturnableQty));
        var owner = (await _s.Owner.Documents.GetAsync(sale.Id)).Data<DocumentDto>();
        Assert.Null(owner.Lines[0].ReturnedQty);                                    // the Owner may not create returns
        Assert.Null(owner.ReturnOf);
        var again = (await _s.Supervisor.Documents.GetAsync(posted.Id)).Data<DocumentDto>();
        Assert.Equal((sale.Id, 1), (again.ReturnOf!.Id, again.Lines[0].ReturnOfLineNo!.Value));
        Assert.Null(again.Lines[0].ReturnedQty);                                    // a return itself is not returned

        (await _s.Admin.Documents.ReverseAsync(posted.Id, "returned in error")).ShouldBeOk();
        var after = (await _s.Supervisor.Documents.GetAsync(sale.Id)).Data<DocumentDto>();
        Assert.Equal((0m, 8m), (after.Lines[0].ReturnedQty, after.Lines[0].ReturnableQty));
    }

    [Fact]
    public async Task Two_returns_for_the_last_quantity_cannot_both_post_the_second_is_409_with_the_figures()
    {
        var (_, _, _, sale) = await SoldAsync();
        var a = await ReturnOfAsync(sale);
        var b = await ReturnOfAsync(sale);
        var approvedA = (await _s.Admin.Documents.ApproveAsync((await _s.Supervisor.Documents.SubmitAsync(a.Id)).Data<DocumentDto>().Id)).Data<DocumentDto>();
        var approvedB = (await _s.Admin.Documents.ApproveAsync((await _s.Supervisor.Documents.SubmitAsync(b.Id)).Data<DocumentDto>().Id)).Data<DocumentDto>(); // both were within what remained when they were submitted

        (await _s.Admin.Documents.PostAsync(approvedA.Id)).ShouldBeOk();
        var refused = (await _s.Admin.Documents.PostAsync(approvedB.Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("more than can be returned", refused.Detail);
        Assert.Equal(DocumentStatus.Approved, (await _s.Admin.Documents.GetAsync(approvedB.Id)).Data<DocumentDto>().Status);
        Assert.Empty(await _s.LedgerAsync(sourceDocId: approvedB.Id));
    }

    // ----- the module goes off with a return open -----
    [Fact]
    public async Task A_return_left_when_the_module_goes_off_cannot_be_edited_submitted_or_posted_but_can_be_cancelled()
    {
        var (_, _, _, sale) = await SoldAsync();
        var draft = await ReturnOfAsync(sale);
        var second = await ReturnOfAsync(sale);
        var third = await ReturnOfAsync(sale);
        var approved = (await _s.Admin.Documents.ApproveAsync((await _s.Supervisor.Documents.SubmitAsync(third.Id)).Data<DocumentDto>().Id)).Data<DocumentDto>();
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] = N'Module.returns'");

        (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft))).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Supervisor.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Admin.Documents.PostAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        var off = (await _s.Supervisor.Documents.GetAsync(draft.Id)).Data<DocumentDto>();
        Assert.Null(off.ReturnOf);
        Assert.DoesNotContain(DocumentActions.Edit, off.AllowedActions);
        Assert.Contains(DocumentActions.Cancel, off.AllowedActions);
        Assert.Equal(DocumentStatus.Cancelled, (await _s.Admin.Documents.CancelAsync(draft.Id, "module off")).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Cancelled, (await _s.Admin.Documents.CancelAsync(approved.Id, "module off")).Data<DocumentDto>().Status);
        Assert.NotNull(second);
    }

    // ----- positions -----
    [Fact]
    public async Task The_lines_of_an_original_of_500_lines_are_numbered_1_to_500_in_the_order_they_are_listed_and_a_return_names_them_by_that_number()
    {
        var warehouse = _s.Reference.DefaultWarehouseId; // the warehouse the Supervisor, the Clerk and the staff work in
        var product = await _s.NewProductAsync();
        var lines = Enumerable.Range(1, Limits.MaxDocumentLines).Select(i => new Line(product.Id, i, 1m)).ToArray(); // quantity i on line i tells the lines apart
        var receipt = await _s.ReceiveAsync(warehouse, lines);

        var draft = await ReturnOfAsync(receipt, _s.Admin);

        Assert.Equal(Limits.MaxDocumentLines, draft.Lines.Count);
        Assert.Equal(Enumerable.Range(1, Limits.MaxDocumentLines), draft.Lines.Select(l => l.ReturnOfLineNo!.Value));
        Assert.Equal(Enumerable.Range(1, Limits.MaxDocumentLines).Select(i => (decimal)i), draft.Lines.Select(l => l.Qty));
        var original = (await _s.Admin.Documents.GetAsync(receipt.Id)).Data<DocumentDto>();
        Assert.Equal(original.Lines.Select(l => l.Qty), draft.Lines.Select(l => l.Qty)); // the original lists its lines in the same order
        // returning only line 500 (quantity 500) and line 250: the numbers pick the right lines
        var pick = (await _s.Admin.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, draft.Purpose, null,
            [new DocumentLineInput(product.Id, 500m, null, null, null, ReturnOfLineNo: 500), new DocumentLineInput(product.Id, 251m, null, null, null, ReturnOfLineNo: 250)]))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(pick.ErrorsFor("lines[1].qty")); // line 250 holds 250, not 251
    }
}
