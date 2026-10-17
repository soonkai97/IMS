using System.Globalization;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>RPT-01: the downloads are real xlsx files with a header row and the numbers the ledger and balances hold.</summary>
[Collection(ApiCollection.Name)]
public class ReportExportTests(ApiFixture fixture)
{
    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Any, CultureInfo.InvariantCulture);

    private static void AssertXlsx(ApiResponse response, string fileName)
    {
        response.ShouldBeOk();
        Assert.Equal(Xlsx.ContentType, response.ContentType);
        Assert.Contains(fileName, response.ContentDisposition);
        Assert.True(response.Bytes.Length > 0);
        Assert.Equal((byte)'P', response.Bytes[0]); // xlsx is a zip: "PK"
        Assert.Equal((byte)'K', response.Bytes[1]);
    }

    [Fact]
    public async Task The_stock_summary_export_has_the_header_row_and_a_row_per_balance()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 2);
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 7, 3m));

        var response = await s.Supervisor.Reports.StockSummaryExportAsync(product.Id);
        // The supervisor only sees WH01, so ask as someone who sees every warehouse.
        var all = await s.Owner.Reports.StockSummaryExportAsync(product.Id);

        AssertXlsx(all, "stock-summary.xlsx");
        var content = Xlsx.Read(all.Bytes);
        Assert.Equal("Stock summary", content.SheetName);
        Assert.Equal(ReportHeaders.StockSummary, content.Headers);
        var row = Assert.Single(content.Rows);
        Assert.Equal(product.Sku, content.Cell(row, "SKU"));
        Assert.Equal(warehouse.Code, content.Cell(row, "Warehouse"));
        Assert.Equal((7m, 3m, 21m), (Number(content.Cell(row, "On hand")), Number(content.Cell(row, "Avg cost")), Number(content.Cell(row, "Value"))));
        Assert.Equal(string.Empty, content.Cell(row, "Low"));
        Assert.Empty(Xlsx.Read(response.Bytes).Rows); // out of the supervisor's scope
    }

    [Fact]
    public async Task The_movements_export_lists_posted_lines_with_document_number_type_and_amount()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 10, 4m));
        var issue = await s.IssueAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 3));

        var response = await s.Admin.Reports.MovementsExportAsync(product.Id);

        AssertXlsx(response, "receipt-issue-detail.xlsx");
        var content = Xlsx.Read(response.Bytes);
        Assert.Equal("Receipt-issue detail", content.SheetName);
        Assert.Equal(ReportHeaders.Movements, content.Headers);
        Assert.Equal(2, content.Rows.Count);
        var receiptRow = content.FindRow("Document", receipt.No)!;
        Assert.Equal(("Receipt", "Posted", product.Sku), (content.Cell(receiptRow, "Type"), content.Cell(receiptRow, "Status"), content.Cell(receiptRow, "SKU")));
        Assert.Equal((10m, 40m), (Number(content.Cell(receiptRow, "Qty")), Number(content.Cell(receiptRow, "Amount"))));
        var issueRow = content.FindRow("Document", issue.No)!;
        Assert.Equal((3m, 12m), (Number(content.Cell(issueRow, "Qty")), Number(content.Cell(issueRow, "Amount")))); // issued at the average cost 4
    }

    [Fact]
    public async Task The_movements_list_shows_reversed_documents_and_ignores_drafts()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 5, 1m));
        var reversed = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 2, 1m));
        (await s.Admin.Documents.ReverseAsync(reversed.Id, "undo the receipt")).ShouldBeOk();
        var draft = await s.DraftReceiptAsync(s.Admin, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 9, 1m));

        var rows = (await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>().Items;

        Assert.Contains(rows, r => r.DocNo == posted.No && r.Status == DocumentStatus.Posted);
        Assert.Contains(rows, r => r.DocNo == reversed.No && r.Status == DocumentStatus.Reversed);
        Assert.DoesNotContain(rows, r => r.DocNo == draft.No);
    }

    [Fact]
    public async Task The_movements_date_filter_includes_today_and_excludes_the_future()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).ToString("yyyy-MM-dd");

        var included = (await s.Admin.GetAsync($"/api/reports/movements?productId={product.Id}&from={today}&to={today}")).Data<PagedResult<MovementRow>>();
        var future = (await s.Admin.GetAsync($"/api/reports/movements?productId={product.Id}&from=2099-01-01")).Data<PagedResult<MovementRow>>();

        Assert.Single(included.Items);
        Assert.Empty(future.Items);
    }

    [Fact]
    public async Task The_ledger_export_has_one_row_per_ledger_entry()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 6, 2m));
        (await s.Admin.Documents.ReverseAsync(receipt.Id, "undo the receipt")).ShouldBeOk();

        var response = await s.Admin.Reports.LedgerExportAsync(product.Id);

        AssertXlsx(response, "stock-ledger.xlsx");
        var content = Xlsx.Read(response.Bytes);
        Assert.Equal(ReportHeaders.Ledger, content.Headers);
        Assert.Equal([-6m, 6m], content.Rows.Select(r => Number(content.Cell(r, "Qty change"))).Order());
        Assert.Equal(["Receipt", "Reversal"], content.Rows.Select(r => content.Cell(r, "Reason")).Order());
        // The date column is a real Excel date shown dd/MM/yyyy hh:mm, not text.
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(response.Bytes));
        var when = workbook.Worksheet(1).Cell(2, 1);
        Assert.Equal(ClosedXML.Excel.XLDataType.DateTime, when.DataType);
        Assert.Equal("dd/mm/yyyy hh:mm", when.Style.DateFormat.Format);
    }

    [Fact]
    public async Task The_low_stock_list_and_export_show_products_below_their_safety_stock()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var low = await s.NewProductAsync(safetyStock: 10);
        var fine = await s.NewProductAsync(safetyStock: 10);
        var noSafety = await s.NewProductAsync(safetyStock: 0);
        await s.ReceiveAsync(warehouse.Id, new Line(low.Id, 3, 1m), new Line(fine.Id, 11, 1m), new Line(noSafety.Id, 1, 1m)); // 11 is above the level of 10; 10 itself would be low

        var list = (await s.Admin.Stock.LowStockAsync(warehouse.Id)).Data<PagedResult<LowStockAlert>>().Items;
        var export = Xlsx.Read((await s.Admin.Reports.LowStockExportAsync()).Bytes);

        var alert = Assert.Single(list, a => a.ProductId == low.Id); // products stocked nowhere are listed too, once each and with no warehouse, whatever other tests left
        Assert.DoesNotContain(list, a => a.ProductId == fine.Id || a.ProductId == noSafety.Id);
        Assert.Equal((low.Id, 3m, 10m, 7m), (alert.ProductId, alert.QtyOnHand, alert.SafetyStock, alert.Shortfall));
        Assert.Equal(ReportHeaders.LowStock, export.Headers);
        var row = export.Rows.Single(r => export.Cell(r, "SKU") == low.Sku && export.Cell(r, "Warehouse") == warehouse.Code); // one row per warehouse; the others never held it
        Assert.Equal((3m, 10m, 7m), (Number(export.Cell(row, "On hand")), Number(export.Cell(row, "Safety stock")), Number(export.Cell(row, "Shortfall"))));
        Assert.DoesNotContain(export.Rows, r => export.Cell(r, "SKU") == fine.Sku && export.Cell(r, "Warehouse") == warehouse.Code);
        Assert.Null(export.FindRow("SKU", noSafety.Sku)); // no safety level: never listed anywhere
    }

    [Fact]
    public async Task A_product_leaves_the_low_stock_list_once_it_is_restocked()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 10);
        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 3, 1m));
        Assert.Single((await s.Admin.Stock.LowStockAsync(warehouse.Id)).Data<PagedResult<LowStockAlert>>().Items, a => a.ProductId == product.Id);

        await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 20, 1m));

        Assert.DoesNotContain((await s.Admin.Stock.LowStockAsync(warehouse.Id)).Data<PagedResult<LowStockAlert>>().Items, a => a.ProductId == product.Id);
    }

    [Fact]
    public async Task An_export_with_no_matching_rows_still_has_the_header_row()
    {
        var s = await StockScenario.CreateAsync(fixture);

        var content = Xlsx.Read((await s.Admin.Reports.StockSummaryExportAsync(productId: Guid.NewGuid())).Bytes);

        Assert.Equal(ReportHeaders.StockSummary, content.Headers);
        Assert.Empty(content.Rows);
    }

    [Fact]
    public async Task Text_that_looks_like_a_formula_is_exported_as_text()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithName("=1+1"));
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        var content = Xlsx.Read((await s.Admin.Reports.StockSummaryExportAsync(product.Id)).Bytes);

        Assert.Equal("=1+1", content.Cell(Assert.Single(content.Rows), "Product"));
    }

    [Fact]
    public async Task The_stock_summary_adds_up_the_value_and_counts_low_products()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var before = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();
        var product = await s.NewProductAsync(safetyStock: 100);
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 4, 25m));

        var after = (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();

        Assert.Equal(before.TotalValue + 100m, after.TotalValue);
        Assert.Equal(before.SkuCount + 1, after.SkuCount);
        Assert.Equal(before.LowCount + 1, after.LowCount);
    }
}
