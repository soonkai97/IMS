using System.Globalization;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>
/// The movements report (page, total and Excel export) has a signed <c>stockEffect</c> next to the unsigned quantity: positive when a movement adds
/// stock, negative when it removes it. A reversed document shows its posting and a reversal row with the opposite sign, so the effects of a
/// product over a period add up to the change in its stock balance.
/// </summary>
[Collection(ApiCollection.Name)]
public class SignedStockEffectTests(ApiFixture fixture)
{
    private const string Reason = "entered by mistake";

    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Any, CultureInfo.InvariantCulture);

    private async Task<List<MovementRow>> RowsAsync(StockScenario s, int productId, int? warehouseId = null, string? extraQuery = null)
    {
        var query = $"/api/reports/movements?productId={productId}&pageSize=200" + (warehouseId is { } w ? $"&warehouseId={w}" : "") + extraQuery;
        return (await s.Admin.GetAsync(query)).Data<PagedResult<MovementRow>>().Items.ToList();
    }

    [Fact]
    public async Task A_receipt_adds_and_an_issue_removes_while_the_quantity_stays_unsigned()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 10, 4m));
        var issue = await s.IssueAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 3));

        var rows = await RowsAsync(s, product.Id);

        var receiptRow = Assert.Single(rows, r => r.DocNo == receipt.No);
        var issueRow = Assert.Single(rows, r => r.DocNo == issue.No);
        Assert.Equal((10m, 10m, 10m, false), (receiptRow.Qty, receiptRow.BaseQty, receiptRow.StockEffect, receiptRow.IsReversal));
        Assert.Equal((3m, 3m, -3m, false), (issueRow.Qty, issueRow.BaseQty, issueRow.StockEffect, issueRow.IsReversal));
    }

    [Fact]
    public async Task Reversing_a_receipt_adds_a_negative_row_and_reversing_an_issue_adds_a_positive_row()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        await s.ReceiveAsync(warehouse, new Line(product.Id, 20, 2m));
        var receipt = await s.ReceiveAsync(warehouse, new Line(product.Id, 6, 2m));
        var issue = await s.IssueAsync(warehouse, new Line(product.Id, 4));
        (await s.Admin.Documents.ReverseAsync(receipt.Id, Reason)).ShouldBeOk();
        (await s.Admin.Documents.ReverseAsync(issue.Id, Reason)).ShouldBeOk();

        var rows = await RowsAsync(s, product.Id);

        Assert.Equal((6m, -6m), EffectsOf(rows, receipt.No));
        Assert.Equal((-4m, 4m), EffectsOf(rows, issue.No));
        var reversal = Assert.Single(rows, r => r.DocNo == receipt.No && r.IsReversal);
        Assert.Equal((DocumentStatus.Reversed, 6m, 6m), (reversal.Status, reversal.Qty, reversal.BaseQty)); // the quantity column is the same on both rows
        Assert.All(rows, r => Assert.Equal(StockDirection.EffectOf(r.Type, r.BaseQty, r.IsReversal), r.StockEffect)); // the SQL sign repeats the domain rule
        Assert.Equal(20m, rows.Sum(r => r.StockEffect)); // only the first receipt is left on the shelf
    }

    private static (decimal Posting, decimal Reversal) EffectsOf(List<MovementRow> rows, string docNo) =>
        (rows.Single(r => r.DocNo == docNo && !r.IsReversal).StockEffect, rows.Single(r => r.DocNo == docNo && r.IsReversal).StockEffect);

    [Fact]
    public async Task After_receipts_issues_a_transfer_and_reversals_the_effects_add_up_to_the_balances_in_the_page_the_export_and_the_ledger()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var from = await s.NewWarehouseAsync();
        var to = await s.NewWarehouseAsync();

        await s.ReceiveAsync(from.Id, new Line(product.Id, 20, 3m));
        await s.IssueAsync(from.Id, new Line(product.Id, 5));
        // A transfer is not a document type yet: it is an issue out of one warehouse and a receipt into the other.
        await s.IssueAsync(from.Id, new Line(product.Id, 4));
        await s.ReceiveAsync(to.Id, new Line(product.Id, 4, 3m));
        var mistakenReceipt = await s.ReceiveAsync(from.Id, new Line(product.Id, 6, 3m));
        (await s.Admin.Documents.ReverseAsync(mistakenReceipt.Id, Reason)).ShouldBeOk();
        var mistakenIssue = await s.IssueAsync(from.Id, new Line(product.Id, 3));
        (await s.Admin.Documents.ReverseAsync(mistakenIssue.Id, Reason)).ShouldBeOk();

        var rows = await RowsAsync(s, product.Id);
        var onHandFrom = await s.OnHandAsync(product.Id, from.Id);
        var onHandTo = await s.OnHandAsync(product.Id, to.Id);

        Assert.Equal((11m, 4m), (onHandFrom, onHandTo));
        Assert.Equal(8, rows.Count); // six postings and the reversal rows of the two reversed documents
        Assert.Equal(onHandFrom + onHandTo, rows.Sum(r => r.StockEffect));
        Assert.Equal(onHandFrom, (await RowsAsync(s, product.Id, from.Id)).Sum(r => r.StockEffect));
        Assert.Equal(onHandTo, (await RowsAsync(s, product.Id, to.Id)).Sum(r => r.StockEffect));
        Assert.Equal(rows.Sum(r => r.StockEffect), (await s.LedgerAsync(product.Id)).Sum(r => r.QtyDelta)); // the report and the ledger agree

        var export = Xlsx.Read((await s.Admin.Reports.MovementsExportAsync(product.Id)).Bytes);
        Assert.Contains("Stock effect", export.Headers);
        Assert.Equal(ReportHeaders.Movements, export.Headers);
        Assert.Equal(rows.Count, export.Rows.Count);
        Assert.Equal(onHandFrom + onHandTo, export.Rows.Sum(r => Number(export.Cell(r, "Stock effect"))));
        Assert.Equal(rows.Sum(r => r.BaseQty), export.Rows.Sum(r => Number(export.Cell(r, "Base qty")))); // the unsigned columns are untouched
        Assert.Equal(rows.Select(r => r.StockEffect), export.Rows.Select(r => Number(export.Cell(r, "Stock effect")))); // same rows, same order
    }

    [Fact]
    public async Task The_date_range_dates_a_reversal_row_by_the_reversal_so_a_period_adds_up_to_its_own_change()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 5, 1m));
        (await s.Admin.Documents.ReverseAsync(receipt.Id, Reason)).ShouldBeOk();
        // The receipt was posted three days ago and reversed today.
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET PostedAt = @at WHERE Id = @id", ("@at", DateTime.UtcNow.AddDays(-3)), ("@id", receipt.Id));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

        var thisPeriod = await RowsAsync(s, product.Id, extraQuery: $"&from={today:yyyy-MM-dd}");
        var earlier = await RowsAsync(s, product.Id, extraQuery: $"&to={today.AddDays(-1):yyyy-MM-dd}");

        Assert.Equal([(-5m, true)], thisPeriod.Select(r => (r.StockEffect, r.IsReversal)));
        Assert.Equal([(5m, false)], earlier.Select(r => (r.StockEffect, r.IsReversal)));
        var total = (await s.Admin.GetAsync($"/api/reports/movements?productId={product.Id}&from={today:yyyy-MM-dd}")).Data<PagedResult<MovementRow>>().Total;
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Pages_of_any_size_walk_the_postings_and_the_reversal_rows_without_skipping_or_repeating_one()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = await s.NewWarehouseAsync();
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 30, 1m));
        for (var i = 0; i < 3; i++)
        {
            var receipt = await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 2 + i, 1m));
            var issue = await s.IssueAsync(warehouse.Id, new Line(product.Id, 1 + i));
            (await s.Admin.Documents.ReverseAsync(receipt.Id, Reason)).ShouldBeOk();
            if (i % 2 == 0) (await s.Admin.Documents.ReverseAsync(issue.Id, Reason)).ShouldBeOk();
        }
        var whole = (await RowsAsync(s, product.Id, warehouse.Id)).Select(r => (r.DocNo, r.IsReversal)).ToList();

        foreach (var pageSize in new[] { 1, 2, 3, 4 })
        {
            var walked = new List<(string, bool)>();
            for (var page = 1; page <= 20; page++)
            {
                var result = (await s.Admin.GetAsync($"/api/reports/movements?productId={product.Id}&warehouseId={warehouse.Id}&page={page}&pageSize={pageSize}")).Data<PagedResult<MovementRow>>();
                walked.AddRange(result.Items.Select(r => (r.DocNo, r.IsReversal)));
                Assert.Equal(whole.Count, result.Total);
                if (result.Items.Count < pageSize) break;
            }
            Assert.Equal(whole, walked);
        }
        Assert.Equal(whole.Count, whole.Distinct().Count());
        Assert.Equal(12, whole.Count); // 7 postings, 3 reversed receipts and 2 reversed issues
    }
}
