using System.Net;
using ClosedXML.Excel;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Import;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Money;

/// <summary>
/// Product-owner decision: money has exactly 2 decimal places and never a fraction of a cent. A unit cost is entered with at most 2 decimals; a line amount is
/// the base quantity times the unit cost rounded half away from zero; a document total is the sum of the rounded line amounts; a stock value is the quantity
/// times the (more precise) average, rounded the same way; every API value and Excel cell of money is rounded to 2 decimals. Quantities are untouched.
/// </summary>
[Collection(ApiCollection.Name)]
public class MoneyPrecisionTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static string CostField(ProblemBody problem) => Assert.Single(problem.ErrorsFor("UnitCost"));

    // ----- unit cost input -----
    [Theory]
    [InlineData("1.234")]
    [InlineData("0.001")]
    [InlineData("10.005")]
    public async Task A_unit_cost_with_a_fraction_of_a_cent_is_refused_with_a_clear_message(string cost)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1, decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture))).Build();

        var created = await s.Admin.Documents.CreateReceiptAsync(request);

        Assert.Equal(Messages.CostTooPrecise, CostField(created.ShouldBeProblem(HttpStatusCode.BadRequest)));
        Assert.Equal("Unit cost can have at most 2 decimal places.", Messages.CostTooPrecise);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("1.2")]
    [InlineData("1.20")]
    [InlineData("0.01")]
    [InlineData("0")]
    public async Task A_unit_cost_of_up_to_two_decimals_is_accepted(string cost)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture), draft.TotalAmount);
    }

    [Fact]
    public async Task Editing_a_draft_applies_the_same_rule()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1, 2.505m)).Build();

        var updated = await s.Admin.Documents.UpdateAsync(draft.Id, request);

        Assert.Equal(Messages.CostTooPrecise, CostField(updated.ShouldBeProblem(HttpStatusCode.BadRequest)));
    }

    [Fact]
    public async Task The_opening_stock_import_refuses_a_cost_with_a_fraction_of_a_cent_in_the_preview_and_creates_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", ["SKU", "Warehouse", "Qty", "UnitCost"],
            [product.Sku, fixture.Reference.DefaultWarehouseCode, 10, 2.5],
            [product.Sku, fixture.Reference.DefaultWarehouseCode, 10, 2.505]);

        var preview = (await s.Admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();
        var commit = await s.Admin.Import.OpeningStockAsync(file, commit: true);

        Assert.Equal([true, false], preview.Rows.Select(r => r.Ok));
        Assert.Equal(Messages.CostTooPrecise, Assert.Single(preview.Rows[1].Errors));
        commit.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_approval_threshold_is_money_too()
    {
        var s = await StockScenario.CreateAsync(fixture);

        var refused = await s.Admin.Settings.CreateRuleAsync(new ApprovalRuleSaveRequest(DocumentType.Issue, 100.005m, null, Roles.Supervisor, IsActive: false));

        Assert.Equal([Messages.AmountTooPrecise], refused.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("AmountThreshold"));
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 100.50m, null, Roles.Supervisor);
    }

    // ----- line amounts and totals -----
    [Fact]
    public async Task A_document_total_is_the_sum_of_the_rounded_line_amounts()
    {
        // Three lines of 1.5 x 0.01 = 0.015 each: every line is 0.02, so the document is 0.06 (the rounded sum of the exact amounts would be 0.05).
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();
        var c = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(a.Id, 1.5m, 0.01m), new Line(b.Id, 1.5m, 0.01m), new Line(c.Id, 1.5m, 0.01m));

        Assert.Equal(0.06m, draft.TotalAmount);
        var listed = (await s.Admin.Documents.ListAsync(search: draft.No)).Data<PagedResult<DocumentListItem>>().Items.Single();
        Assert.Equal(0.06m, listed.TotalAmount);
        var posted = await s.CompleteAsync(await s.SubmitAsync(s.Admin, draft));
        Assert.Equal(0.06m, posted.TotalAmount);
    }

    [Fact]
    public async Task The_movements_report_and_its_export_show_the_rounded_line_amounts_with_a_money_format()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1.5m, 0.01m));

        var page = (await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>();
        var export = await s.Admin.Reports.MovementsExportAsync(product.Id);

        var row = Assert.Single(page.Items);
        Assert.Equal((0.01m, 0.02m), (row.UnitCost, row.Amount)); // 1.5 x 0.01 = 0.015, rounded away from zero
        using var workbook = new XLWorkbook(new MemoryStream(export.Bytes));
        var sheet = workbook.Worksheets.First();
        var headers = Enumerable.Range(1, sheet.LastColumnUsed()!.ColumnNumber()).Select(i => sheet.Cell(1, i).GetString()).ToList();
        var amount = sheet.Cell(2, headers.IndexOf("Amount") + 1);
        Assert.Equal(0.02, amount.GetDouble());
        Assert.Equal("#,##0.00", amount.Style.NumberFormat.Format);
        Assert.Equal("#,##0.00", sheet.Cell(2, headers.IndexOf("Unit cost") + 1).Style.NumberFormat.Format);
        Assert.NotEqual("#,##0.00", sheet.Cell(2, headers.IndexOf("Qty") + 1).Style.NumberFormat.Format); // a quantity is not money
    }

    // ----- the average has more places than a person sees -----
    /// <summary>One piece left at an average of 10.005 (10.00 and 10.01 blended): the half-cent boundary of an average cost and a stock value.</summary>
    private async Task<(StockScenario S, int ProductId)> OnePieceAtAHalfCentAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 10.00m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 10.01m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 1));
        return (s, product.Id);
    }

    [Fact]
    public async Task A_stock_value_at_the_half_cent_rounds_away_from_zero_and_the_summary_is_the_sum_of_the_rounded_rows()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var before = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().TotalValue;
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 10.00m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 10.01m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 1));

        var row = (await s.BalanceAsync(product.Id, Warehouse))!;
        var after = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().TotalValue;

        Assert.Equal(1m, row.QtyOnHand);
        Assert.Equal(10.01m, row.AvgCost);   // 10.005 inside, 10.01 shown
        Assert.Equal(10.01m, row.Value);     // 1 x 10.005 rounded half away from zero (to-even would say 10.00)
        Assert.Equal(10.01m, after - before);
    }

    [Fact]
    public async Task The_issue_that_leaves_at_the_half_cent_is_booked_and_shown_rounded()
    {
        var (s, productId) = await OnePieceAtAHalfCentAsync();

        var ledger = await s.LedgerAsync(productId, Warehouse);
        var movements = (await s.Admin.Reports.MovementsAsync(productId)).Data<PagedResult<MovementRow>>().Items;

        Assert.All(ledger.Where(l => l.Reason == LedgerReason.Issue), l => Assert.Equal(10.01m, l.UnitCost));
        var issue = movements.Single(m => m.Type == DocumentType.Issue);
        Assert.Equal((10.01m, 10.01m), (issue.UnitCost, issue.Amount));
    }

    [Theory]
    [InlineData("10.01", true)]
    [InlineData("10.02", false)]
    public async Task An_estimated_amount_is_rounded_before_it_meets_an_approval_threshold(string threshold, bool fires)
    {
        // One piece at an average of 10.005 is estimated at 10.01 (not 10.005): a rule from 10.01 fires, a rule from 10.02 does not.
        var (s, productId) = await OnePieceAtAHalfCentAsync();
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture), null, Roles.Supervisor);

        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 1)));

        var seen = (await s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
        Assert.Equal(10.01m, seen.TotalAmount);
        Assert.True(seen.AmountIsEstimate);
        Assert.Equal(fires, seen.RequiresApproval);
        if (fires) Assert.Equal(10.01m, Assert.Single(seen.ApprovalTriggers).Actual);
    }

    [Fact]
    public async Task The_stock_summary_export_shows_the_average_and_the_value_rounded_with_a_money_format()
    {
        var (s, productId) = await OnePieceAtAHalfCentAsync();

        var export = await s.Owner.Reports.StockSummaryExportAsync(productId);

        using var workbook = new XLWorkbook(new MemoryStream(export.Bytes));
        var sheet = workbook.Worksheets.First();
        var headers = Enumerable.Range(1, sheet.LastColumnUsed()!.ColumnNumber()).Select(i => sheet.Cell(1, i).GetString()).ToList();
        foreach (var column in new[] { "Avg cost", "Value" })
        {
            var cell = sheet.Cell(2, headers.IndexOf(column) + 1);
            Assert.Equal(10.01, cell.GetDouble());
            Assert.Equal("#,##0.00", cell.Style.NumberFormat.Format);
        }
    }

    [Fact]
    public async Task The_amount_in_the_audit_entry_of_a_submission_is_the_rounded_total()
    {
        var (s, productId) = await OnePieceAtAHalfCentAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 1)));

        var entries = (await s.Admin.Audit.QueryAsync(entityId: submitted.No, pageSize: 50)).Data<AuditPage>().Items;

        var submission = entries.Single(e => e.Action == AuditActions.DocumentSubmitted);
        Assert.Contains("\"Amount\":10.01", submission.After); // 1 x 10.005, rounded; the audit log is read as JSON text, so a longer number would show
    }

    // ----- nothing on the wire has a fraction of a cent -----
    [Fact]
    public async Task No_response_carries_a_money_value_with_a_fraction_of_a_cent()
    {
        var (s, productId) = await OnePieceAtAHalfCentAsync();
        var carton = await s.NewProductAsync(safetyStock: 100, customize: p => p.WithConversion(fixture.Reference.CartonUnitId, 3m)); // low, so the "low only" list shows it too
        var receipt = await s.ReceiveAsync(Warehouse, new Line(carton.Id, 1, 10.00m, fixture.Reference.CartonUnitId)); // 10.00 per carton of 3: 3.333333 per piece
        var issue = await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, Warehouse, new Line(productId, 1)));

        var responses = new[]
        {
            await s.Admin.Documents.GetAsync(receipt.Id), await s.Admin.Documents.GetAsync(issue.Id), await s.Admin.Documents.ListAsync(search: receipt.No),
            await s.Admin.Stock.QueryAsync(carton.Id), await s.Admin.Stock.QueryAsync(carton.Id, lowOnly: true), await s.Admin.Stock.QueryAsync(productId),
            await s.Admin.Stock.LedgerAsync(carton.Id), await s.Admin.Stock.LedgerAsync(productId),
            await s.Admin.Stock.SummaryAsync(), await s.Admin.Reports.MovementsAsync(carton.Id), await s.Admin.Reports.MovementsAsync(productId),
            await s.Admin.Settings.ListRulesAsync()
        };

        foreach (var response in responses)
            Assert.Empty(MoneyScan.SubCentValues(response.ShouldBeOk().Body));
    }
}
