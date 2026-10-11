using System.Net;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>The everyday path: a receipt puts stock in, an issue takes it out, and the ledger and balance agree.</summary>
[Collection(ApiCollection.Name)]
public class ReceiptAndIssueFlowTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task A_receipt_goes_from_draft_to_posted_and_adds_stock_with_one_ledger_entry()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 10, 4.5m));
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        Assert.Matches(new Regex(@"^RCV-WH01-\d{4}-\d{4}$"), draft.No);
        Assert.Equal(10, draft.TotalQty);
        Assert.Equal(45m, draft.TotalAmount);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));

        var submitted = await s.SubmitAsync(s.Clerk, draft);
        Assert.Equal(DocumentStatus.Submitted, submitted.Status);
        Assert.False(submitted.RequiresApproval);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));

        var posted = (await s.Staff.Documents.PostAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.NotNull(posted.PostedAt);
        Assert.Equal("staff", posted.PostedBy);
        var balance = await s.BalanceAsync(product.Id, Warehouse);
        Assert.Equal(10, balance!.QtyOnHand);
        Assert.Equal(4.5m, balance.AvgCost);
        var ledger = await s.LedgerAsync(sourceDocId: draft.Id);
        var entry = Assert.Single(ledger);
        Assert.Equal((product.Sku, 10m, 4.5m, LedgerReason.Receipt), (entry.Sku, entry.QtyDelta, entry.UnitCost, entry.Reason));
        Assert.Equal(draft.No, entry.SourceDocNo);
    }

    [Fact]
    public async Task An_issue_takes_stock_out_at_the_average_cost()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 5m));

        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 30));
        Assert.Matches(new Regex(@"^ISS-WH01-\d{4}-\d{4}$"), draft.No);
        Assert.Equal(IssuePurpose.Sale, draft.Purpose);
        var submitted = await s.SubmitAsync(s.Clerk, draft);
        Assert.False(submitted.RequiresApproval);
        var posted = (await s.Staff.Documents.PostAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal(70, await s.OnHandAsync(product.Id, Warehouse));
        var entry = Assert.Single(await s.LedgerAsync(sourceDocId: draft.Id));
        Assert.Equal((-30m, 5m, LedgerReason.Issue), (entry.QtyDelta, entry.UnitCost, entry.Reason));
        Assert.Null(Assert.Single(posted.Lines).UnitCost); // the Warehouse staff who posted it do not see cost
        Assert.Equal(5m, Assert.Single((await s.GetAsync(draft.Id)).Lines).BaseUnitCost);
    }

    [Fact]
    public async Task The_average_cost_follows_receipts_and_is_unchanged_by_issues()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 10m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 20m));
        Assert.Equal(15m, (await s.BalanceAsync(product.Id, Warehouse))!.AvgCost);

        var issue = await s.IssueAsync(Warehouse, new Line(product.Id, 5));
        Assert.Equal(15m, Assert.Single(issue.Lines).BaseUnitCost);
        var afterIssue = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((15m, 15m), (afterIssue.QtyOnHand, afterIssue.AvgCost));

        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 30m));
        var afterThird = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((20m, 18.75m), (afterThird.QtyOnHand, afterThird.AvgCost)); // (15 x 15 + 5 x 30) / 20
        Assert.Equal(afterThird.QtyOnHand * afterThird.AvgCost, afterThird.Value);
    }

    [Fact]
    public async Task Quantities_in_a_larger_unit_are_converted_to_the_base_unit()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 12));

        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 2, UnitCost: 120m, UomId: fixture.Reference.CartonUnitId));

        var line = Assert.Single(receipt.Lines);
        Assert.Equal((2m, fixture.Reference.CartonUnitId, 24m, 120m, 10m, 240m), (line.Qty, line.UomId, line.BaseQty, line.UnitCost, line.BaseUnitCost, line.Amount)); // per carton, per piece, the line
        Assert.Equal(24, await s.OnHandAsync(product.Id, Warehouse));

        await s.IssueAsync(Warehouse, new Line(product.Id, 1, UomId: fixture.Reference.CartonUnitId));
        Assert.Equal(12, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_unit_without_a_conversion_for_the_product_is_refused()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithLines(new Line(product.Id, 1, 1m, fixture.Reference.CartonUnitId)).Build();

        var response = await s.Admin.Documents.CreateReceiptAsync(request);

        response.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_receipt_can_carry_several_products_and_each_gets_its_own_ledger_entry()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();

        var receipt = await s.ReceiveAsync(Warehouse, new Line(a.Id, 3, 2m), new Line(b.Id, 4, 3m));

        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(7, receipt.TotalQty);
        Assert.Equal(18m, receipt.TotalAmount);
        Assert.Equal(2, (await s.LedgerAsync(sourceDocId: receipt.Id)).Count);
        Assert.Equal((3m, 4m), (await s.OnHandAsync(a.Id, Warehouse), await s.OnHandAsync(b.Id, Warehouse)));
    }

    // ---------- editing a draft ----------

    [Fact]
    public async Task A_draft_can_be_edited_and_its_totals_follow()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(a.Id, 1, 1m));

        var edited = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithRemark("Edited")
            .WithLines(new Line(a.Id, 5, 2m), new Line(b.Id, 1, 10m)).Build();
        var updated = (await s.Clerk.Documents.UpdateAsync(draft.Id, edited)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Draft, updated.Status);
        Assert.Equal("Edited", updated.Remark);
        Assert.Equal(2, updated.Lines.Count);
        Assert.Equal(20m, updated.TotalAmount);
        Assert.Equal(draft.No, updated.No);
    }

    [Fact]
    public async Task The_warehouse_of_an_existing_document_cannot_change()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));

        var move = new DocumentBuilder(other.Id).WithLines(new Line(product.Id, 1, 1m)).Build();

        (await s.Admin.Documents.UpdateAsync(draft.Id, move)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_submitted_document_can_no_longer_be_edited()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        await s.SubmitAsync(s.Clerk, draft);

        var response = await s.Clerk.Documents.UpdateAsync(draft.Id, new DocumentBuilder(Warehouse).WithLines(new Line(product.Id, 99, 1m)).Build());

        Assert.Contains("can no longer be edited", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(1, Assert.Single((await s.GetAsync(draft.Id)).Lines).Qty);
    }

    // ---------- validation ----------

    [Fact]
    public async Task A_document_without_lines_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);

        var response = await s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).Build());

        Assert.NotEmpty(response.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, -1)]
    public async Task Non_positive_quantities_and_negative_costs_are_400(double qty, double cost)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithLines(new Line(product.Id, (decimal)qty, (decimal)cost)).Build();

        (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_product_or_warehouse_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var unknownProduct = new DocumentBuilder(Warehouse).WithLines(new Line(9_999_999, 1, 1m)).Build();
        var unknownWarehouse = new DocumentBuilder(9_999_999).WithLines(new Line(product.Id, 1, 1m)).Build();
        var noWarehouse = new DocumentBuilder(0).WithLines(new Line(product.Id, 1, 1m)).Build();

        (await s.Admin.Documents.CreateReceiptAsync(unknownProduct)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.CreateReceiptAsync(unknownWarehouse)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.CreateReceiptAsync(noWarehouse)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_missing_document_is_404_for_every_action()
    {
        var s = await StockScenario.CreateAsync(fixture);
        const int missing = 9_999_999;

        (await s.Admin.Documents.GetAsync(missing)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Admin.Documents.SubmitAsync(missing)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Admin.Documents.PostAsync(missing)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Admin.Documents.CancelAsync(missing)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Admin.Documents.ReverseAsync(missing, "not needed here")).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Owner.Documents.ApproveAsync(missing)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await s.Owner.Documents.RejectAsync(missing, "not needed here")).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    // ---------- lists ----------

    [Fact]
    public async Task The_document_list_filters_by_type_status_and_number()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 1m));
        var draftIssue = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 1));

        var byNumber = (await s.Admin.Documents.ListAsync(search: receipt.No)).Data<PagedResult<DocumentListItem>>();
        var postedReceipts = (await s.Admin.Documents.ListAsync(DocumentType.Receipt, DocumentStatus.Posted, search: receipt.No)).Data<PagedResult<DocumentListItem>>();
        var postedIssues = (await s.Admin.Documents.ListAsync(DocumentType.Issue, DocumentStatus.Posted, search: draftIssue.No)).Data<PagedResult<DocumentListItem>>();
        var draftIssues = (await s.Admin.Documents.ListAsync(DocumentType.Issue, DocumentStatus.Draft, search: draftIssue.No)).Data<PagedResult<DocumentListItem>>();

        var item = Assert.Single(byNumber.Items);
        Assert.Equal((receipt.Id, DocumentType.Receipt, DocumentStatus.Posted, 5m), (item.Id, item.Type, item.Status, item.TotalQty));
        Assert.Single(postedReceipts.Items);
        Assert.Empty(postedIssues.Items);
        Assert.Single(draftIssues.Items);
        Assert.Equal(1, byNumber.Total);
    }
}
