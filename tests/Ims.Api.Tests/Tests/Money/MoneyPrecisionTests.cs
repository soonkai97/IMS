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
        Assert.Equal("#,##0.0000", sheet.Cell(2, headers.IndexOf("Unit cost") + 1).Style.NumberFormat.Format); // a unit price: 4 decimals
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
        Assert.Equal(10.005m, row.AvgCost);  // a unit price is shown with 4 decimals
        Assert.Equal(10.01m, row.Value);     // 1 x 10.005 rounded half away from zero (to-even would say 10.00)
        Assert.Equal(10.01m, after - before);
    }

    [Fact]
    public async Task The_issue_that_leaves_at_the_half_cent_is_booked_and_shown_rounded()
    {
        var (s, productId) = await OnePieceAtAHalfCentAsync();

        var ledger = await s.LedgerAsync(productId, Warehouse);
        var movements = (await s.Admin.Reports.MovementsAsync(productId)).Data<PagedResult<MovementRow>>().Items;

        Assert.All(ledger.Where(l => l.Reason == LedgerReason.Issue), l => Assert.Equal(10.005m, l.UnitCost));
        var issue = movements.Single(m => m.Type == DocumentType.Issue);
        Assert.Equal((10.005m, 10.01m), (issue.UnitCost, issue.Amount)); // the unit price shows 4 decimals, the amount is money
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
        var average = sheet.Cell(2, headers.IndexOf("Avg cost") + 1);
        var value = sheet.Cell(2, headers.IndexOf("Value") + 1);
        Assert.Equal((10.005, "#,##0.0000"), (average.GetDouble(), average.Style.NumberFormat.Format)); // a unit price: 4 decimals
        Assert.Equal((10.01, "#,##0.00"), (value.GetDouble(), value.Style.NumberFormat.Format));        // money: 2
    }

    [Fact]
    public async Task A_negative_stock_value_at_the_half_cent_rounds_away_from_zero_in_sql_and_in_the_summary()
    {
        // 2 issued of 1 on hand at an average of 10.005: -1 piece, worth -10.01 as +1 is worth +10.01.
        var (s, productId) = await OnePieceAtAHalfCentAsync();
        var before = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().TotalValue;
        await GoBelowZeroAsync(s, new Line(productId, 2));

        var row = (await s.BalanceAsync(productId, Warehouse))!;
        var after = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>().TotalValue;

        Assert.Equal((-1m, 10.005m, -10.01m), (row.QtyOnHand, row.AvgCost, row.Value));
        Assert.Equal(-20.02m, after - before); // from +10.01 to -10.01
    }

    private async Task GoBelowZeroAsync(StockScenario s, Line line)
    {
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true, allowSelfApproval: true);
        var submitted = await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, Warehouse, line));
        (await s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "Counted by hand, delivery is late")).ShouldBeOk();
        (await s.Admin.Documents.PostAsync(submitted.Id)).ShouldBeOk();
    }

    // ----- a derived cost per base unit is a unit price: 4 decimals, while the amounts stay money -----
    [Fact]
    public async Task A_carton_of_a_thousand_at_three_shows_point_zero_zero_three_zero_a_piece_and_the_amounts_stay_right()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 1000));

        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 3.00m, fixture.Reference.CartonUnitId));

        var line = Assert.Single(receipt.Lines);
        Assert.Equal((3.00m, 0.0030m, 3.00m), (line.UnitCost, line.BaseUnitCost, line.Amount)); // per carton, per piece, the line
        Assert.Equal(3.00m, receipt.TotalAmount);
        var row = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((1000m, 0.0030m, 3.00m), (row.QtyOnHand, row.AvgCost, row.Value));
        var ledger = Assert.Single(await s.LedgerAsync(product.Id, Warehouse));
        Assert.Equal(0.0030m, ledger.UnitCost);
        var movement = Assert.Single((await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>().Items);
        Assert.Equal((0.0030m, 3.00m), (movement.UnitCost, movement.Amount));

        var summary = ReadSheet((await s.Owner.Reports.StockSummaryExportAsync(product.Id)).Bytes);
        Assert.Equal((0.003, "#,##0.0000"), (summary.Cell("Avg cost").GetDouble(), summary.Cell("Avg cost").Style.NumberFormat.Format));
        Assert.Equal((3.0, "#,##0.00"), (summary.Cell("Value").GetDouble(), summary.Cell("Value").Style.NumberFormat.Format));
        var movements = ReadSheet((await s.Admin.Reports.MovementsExportAsync(product.Id)).Bytes);
        Assert.Equal("#,##0.0000", movements.Cell("Unit cost").Style.NumberFormat.Format);
        Assert.Equal("#,##0.00", movements.Cell("Amount").Style.NumberFormat.Format);
    }

    /// <summary>The first data row of the first sheet, by header.</summary>
    private sealed class Sheet(IXLWorksheet sheet, List<string> headers)
    {
        public IXLCell Cell(string header) => sheet.Cell(2, headers.IndexOf(header) + 1);
    }

    private static Sheet ReadSheet(byte[] bytes)
    {
        var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();
        return new Sheet(sheet, Enumerable.Range(1, sheet.LastColumnUsed()!.ColumnNumber()).Select(i => sheet.Cell(1, i).GetString()).ToList());
    }

    // ----- review M1: the line amount is stored, computed once, with no division through the base unit -----
    [Theory]
    [InlineData(100_000, 10.00, 7, 1_000_000.00)]     // per piece 1.428571: rebuilt from it the line was 999,999.70
    [InlineData(50_000, 12.34, 144, 617_000.00)]      // rebuilt: 616,996.80
    [InlineData(1_000_000_000, 1.00, 3, 1_000_000_000.00)]
    public async Task A_line_in_cartons_is_priced_by_cartons_times_the_carton_price(int cartons, double price, int factor, double expected)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, factor));

        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, cartons, (decimal)price, fixture.Reference.CartonUnitId));

        Assert.Equal((decimal)expected, draft.TotalAmount);
        Assert.Equal((decimal)expected, Assert.Single(draft.Lines).Amount);
        Assert.Equal((decimal)price, Assert.Single(draft.Lines).UnitCost);
        var listed = (await s.Admin.Documents.ListAsync(search: draft.No)).Data<PagedResult<DocumentListItem>>().Items.Single();
        Assert.Equal((decimal)expected, listed.TotalAmount);
    }

    [Fact]
    public async Task A_posted_carton_receipt_keeps_its_amount_in_the_movements_report_and_the_export()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 7));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100_000, 10.00m, fixture.Reference.CartonUnitId));

        var row = Assert.Single((await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>().Items);
        var export = ReadSheet((await s.Admin.Reports.MovementsExportAsync(product.Id)).Bytes);

        Assert.Equal(1_000_000.00m, row.Amount);
        Assert.Equal(1_000_000.00, export.Cell("Amount").GetDouble());
        Assert.Equal(1.4286m, row.UnitCost); // per piece, a unit price with 4 decimals
    }

    // ----- review M3: reading a draft and saving it again does not re-price a line in a carton -----
    [Fact]
    public async Task Reading_a_draft_and_saving_it_back_keeps_a_carton_line_as_it_was()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 3));
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 10.00m, fixture.Reference.CartonUnitId));
        var line = Assert.Single(draft.Lines);
        Assert.Equal((10.00m, 3.3333m, 10.00m), (line.UnitCost, line.BaseUnitCost, line.Amount));

        // What an editor does: take the line as it was read and send it back.
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, line.Qty, line.UnitCost, line.UomId)).Build();
        var saved = (await s.Admin.Documents.UpdateAsync(draft.Id, request, draft.ConcurrencyToken)).Data<DocumentDto>();
        var again = (await s.Admin.Documents.GetAsync(draft.Id)).Data<DocumentDto>();

        foreach (var document in new[] { saved, again })
        {
            var kept = Assert.Single(document.Lines);
            Assert.Equal((10.00m, 3.3333m, 10.00m), (kept.UnitCost, kept.BaseUnitCost, kept.Amount));
            Assert.Equal(10.00m, document.TotalAmount);
        }
    }

    [Fact]
    public async Task An_issue_line_is_at_the_average_it_has_no_cost_to_edit_and_shows_the_average_and_the_amount()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 12));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 120, 2.5m));

        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 2, null, fixture.Reference.CartonUnitId));

        var line = Assert.Single(draft.Lines);
        Assert.Null(line.UnitCost);
        Assert.True(line.CostFromAverage);
        Assert.Equal((2.5m, 60.00m), (line.BaseUnitCost, line.Amount)); // 2 cartons of 12 pieces at 2.50 a piece
        Assert.Equal(60.00m, draft.TotalAmount);
    }

    // ----- review MA1: a line at the average is not re-priced by opening the draft and saving it -----
    /// <summary>1,000 pieces at 0.0030 (a carton of 1,000 at 3.00 in stock), then a Warehouse staff receipt of 1,000 pieces: valued at the average, 3.00.</summary>
    private async Task<(StockScenario S, int ProductId, DocumentDto StaffDraft)> StaffReceiptAtASubCentAverageAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 1000));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 3.00m, fixture.Reference.CartonUnitId));
        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1000));
        return (s, product.Id, (await s.Supervisor.Documents.GetAsync(draft.Id)).Data<DocumentDto>());
    }

    private static DocumentSaveRequest SaveBack(StockScenario s, int warehouse, DocumentDto read) =>
        new DocumentBuilder(warehouse).WithPartner(s.Reference.SupplierId).WithLines(read.Lines.Select(l => new Line(l.ProductId, l.Qty, l.UnitCost, l.UomId)).ToArray()).Build();

    [Fact]
    public async Task A_staff_receipt_read_by_a_supervisor_has_no_cost_to_edit_and_saving_it_back_keeps_the_average()
    {
        var (s, productId, read) = await StaffReceiptAtASubCentAverageAsync();
        var line = Assert.Single(read.Lines);
        Assert.Equal((null, 0.0030m, 3.00m, true), (line.UnitCost, line.BaseUnitCost, line.Amount, line.CostFromAverage));

        var saved = (await s.Supervisor.Documents.UpdateAsync(read.Id, SaveBack(s, Warehouse, read), read.ConcurrencyToken)).Data<DocumentDto>();
        var again = Assert.Single((await s.Supervisor.Documents.GetAsync(read.Id)).Data<DocumentDto>().Lines);

        foreach (var kept in new[] { Assert.Single(saved.Lines), again })
            Assert.Equal((null, 0.0030m, 3.00m, true), (kept.UnitCost, kept.BaseUnitCost, kept.Amount, kept.CostFromAverage)); // not 0.00 / 0.00
        await s.CompleteAsync(await s.SubmitAsync(s.Supervisor, saved));
        var row = (await s.BalanceAsync(productId, Warehouse))!;
        Assert.Equal((2000m, 0.0030m, 6.00m), (row.QtyOnHand, row.AvgCost, row.Value)); // the receipt did not pull the average down
    }

    [Fact]
    public async Task An_average_of_1_2349_times_a_thousand_pieces_is_1234_90_before_and_after_the_save()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 51, 1.23m), new Line(product.Id, 49, 1.24m)); // (62.73 + 60.76) / 100 = 1.2349
        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1000));
        var read = (await s.Supervisor.Documents.GetAsync(draft.Id)).Data<DocumentDto>();
        Assert.Equal((1234.90m, 1234.90m), (Assert.Single(read.Lines).Amount, read.TotalAmount));

        var saved = (await s.Supervisor.Documents.UpdateAsync(read.Id, SaveBack(s, Warehouse, read), read.ConcurrencyToken)).Data<DocumentDto>();

        Assert.Equal((1234.90m, 1234.90m, true), (Assert.Single(saved.Lines).Amount, saved.TotalAmount, Assert.Single(saved.Lines).CostFromAverage)); // not 1,230.00
    }

    [Fact]
    public async Task A_cost_typed_on_a_line_at_the_average_makes_it_a_typed_cost()
    {
        var (s, _, read) = await StaffReceiptAtASubCentAverageAsync();
        var line = Assert.Single(read.Lines);
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(line.ProductId, line.Qty, 2.00m, line.UomId)).Build();

        var saved = Assert.Single((await s.Supervisor.Documents.UpdateAsync(read.Id, request, read.ConcurrencyToken)).Data<DocumentDto>().Lines);

        Assert.Equal((2.00m, 2000.00m, false), (saved.UnitCost, saved.Amount, saved.CostFromAverage));
    }

    [Fact]
    public async Task A_new_line_without_a_cost_in_a_draft_that_has_a_line_at_the_average_is_a_typed_zero()
    {
        var (s, productId, read) = await StaffReceiptAtASubCentAverageAsync();
        var other = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId)
            .WithLines(new Line(productId, 1000), new Line(other.Id, 5)).Build();

        var saved = (await s.Supervisor.Documents.UpdateAsync(read.Id, request, read.ConcurrencyToken)).Data<DocumentDto>();

        Assert.True(saved.Lines.Single(l => l.ProductId == productId).CostFromAverage);   // matched to the line that was at the average
        var added = saved.Lines.Single(l => l.ProductId == other.Id);
        Assert.Equal((0m, 0m, false), (added.UnitCost, added.Amount, added.CostFromAverage)); // no earlier line: a cost left out is zero, as before
    }

    [Fact]
    public async Task A_supervisors_receipt_without_a_cost_on_a_line_is_a_typed_zero_as_before()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Supervisor, Warehouse, new Line(product.Id, 5));

        var line = Assert.Single(draft.Lines);
        Assert.Equal((0m, 0m, false), (line.UnitCost, line.Amount, line.CostFromAverage));
    }

    // ----- review m1..m3: where a cost is judged, how an import cell is read -----
    [Fact]
    public async Task A_sub_cent_cost_is_judged_only_where_it_is_used()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 4m));

        var issue = await s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(Warehouse).WithPartner(s.Reference.CustomerId)
            .WithLines(new Line(product.Id, 1, 1.234m)).Build());                                       // an issue takes the average: the cost is not used
        var staff = await s.Staff.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId)
            .WithLines(new Line(product.Id, 1, 1.234m)).Build());                                       // the Warehouse staff do not enter cost: ignored
        var clerk = await s.Clerk.Documents.CreateReceiptAsync(new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId)
            .WithLines(new Line(product.Id, 1, 1.234m)).Build());                                       // the Clerk's cost is used

        issue.ShouldBeOk();
        staff.ShouldBeOk();
        Assert.Equal(Messages.CostTooPrecise, CostField(clerk.ShouldBeProblem(HttpStatusCode.BadRequest)));
    }

    [Fact]
    public async Task A_line_amount_too_large_for_the_database_is_a_400_with_a_sentence_not_a_500()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1_000_000_000m, 1_000_000_000m)).Build(); // 1e18 > decimal(18,2)

        var created = (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.LineAmountTooLarge, created.Detail);
    }

    [Fact]
    public async Task A_caller_without_cost_view_gets_the_same_answer_whatever_the_hidden_average_is()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var carton = fixture.Reference.CartonUnitId;
        foreach (var average in new[] { 12m, 10m })   // 90,000,000 cartons of 1,000,000 pieces: the line is 1.08e15 at 12, 9e14 at 10 (the limit is just under 1e15)
        {
            var product = await s.NewProductAsync(customize: b => b.WithConversion(carton, 1_000_000));
            await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, average));
            var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 90_000_000m, UomId: carton)).Build();
            var issue = new DocumentBuilder(Warehouse).WithPartner(s.Reference.CustomerId).WithLines(new Line(product.Id, 90_000_000m, UomId: carton)).Build();

            var receipt = await s.Staff.Documents.CreateReceiptAsync(request);
            var drafted = await s.Staff.Documents.CreateIssueAsync(issue);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (receipt.Status, drafted.Status)); // no 400 that depends on the average
            Assert.Null(Assert.Single(receipt.Data<DocumentDto>().Lines).Amount);                              // and nothing of it is shown
        }
    }

    [Fact]
    public async Task A_quantity_that_passes_the_base_quantity_column_is_a_400_not_a_500()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 1_000_000));
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1_000_000_000m, 1m, fixture.Reference.CartonUnitId)).Build(); // 1e15 pieces

        var created = (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.QtyTooLarge, created.Detail);
    }

    [Fact]
    public async Task Every_line_with_a_sub_cent_cost_is_named_not_only_the_first()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId)
            .WithLines(new Line(product.Id, 1, 1.234m), new Line(product.Id, 1, 2.00m), new Line(product.Id, 1, 3.456m)).Build();

        var created = (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(["Data.Lines[0].UnitCost", "Data.Lines[2].UnitCost"], created.Errors!.Keys.Order());
    }

    [Fact]
    public async Task A_negative_sub_cent_cost_gives_one_error_not_two()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(Warehouse).WithPartner(s.Reference.SupplierId).WithLines(new Line(product.Id, 1, -1.234m)).Build();

        var created = (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([Messages.CostNegative], created.ErrorsFor("UnitCost"));
    }

    private static byte[] OpeningStockFile(string sku, string warehouse, Action<IXLCell> costCell)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Opening stock");
        string[] headers = ["SKU", "Warehouse", "Qty", "UnitCost"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Cell(2, 1).Value = sku;
        sheet.Cell(2, 2).Value = warehouse;
        sheet.Cell(2, 3).Value = 5;
        costCell(sheet.Cell(2, 4));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task The_import_reads_the_number_in_a_cell_not_the_text_the_cell_shows()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var code = fixture.Reference.DefaultWarehouseCode;

        // 2.505 shown as 2.5: the text would let a fraction of a cent through.
        var hidden = OpeningStockFile(product.Sku, code, c => { c.Value = 2.505; c.Style.NumberFormat.Format = "0.0"; });
        // 1234.5 shown as 1,234.50: the text, with its comma, would be rejected as "not a number".
        var comma = OpeningStockFile(product.Sku, code, c => { c.Value = 1234.5; c.Style.NumberFormat.Format = "#,##0.00"; });
        // A formula: the number it gives, 5.00.
        var formula = OpeningStockFile(product.Sku, code, c => c.FormulaA1 = "2.5*2");
        // A formula with a fraction of a cent: 0.1 + 2.405.
        var formulaSubCent = OpeningStockFile(product.Sku, code, c => c.FormulaA1 = "0.1+2.405");

        var hiddenResult = (await s.Admin.Import.OpeningStockAsync(hidden, commit: false)).Data<ImportResult>();
        var commaResult = (await s.Admin.Import.OpeningStockAsync(comma, commit: false)).Data<ImportResult>();
        var formulaResult = (await s.Admin.Import.OpeningStockAsync(formula, commit: false)).Data<ImportResult>();
        var subCentResult = (await s.Admin.Import.OpeningStockAsync(formulaSubCent, commit: false)).Data<ImportResult>();

        Assert.Equal(Messages.CostTooPrecise, Assert.Single(hiddenResult.Rows[0].Errors));
        Assert.True(commaResult.Rows[0].Ok, string.Join("; ", commaResult.Rows[0].Errors));
        Assert.True(formulaResult.Rows[0].Ok, string.Join("; ", formulaResult.Rows[0].Errors));
        Assert.Equal(Messages.CostTooPrecise, Assert.Single(subCentResult.Rows[0].Errors));
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
