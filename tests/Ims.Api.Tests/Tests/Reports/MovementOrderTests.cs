using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>
/// RPT-01: the receipt/issue detail is newest posting first; documents posted at the same instant come newest document id first, and the
/// lines of a document stay in entry order. The page, every page size and the export must all show that one order, so a walk through the
/// pages never skips or repeats a row.
/// </summary>
[Collection(ApiCollection.Name)]
public class MovementOrderTests(ApiFixture fixture)
{
    private sealed record Scenario(int WarehouseId, string Earlier, string FirstTied, string SecondTied, string SkuA, string SkuB);

    /// <summary>
    /// Three posted documents in a fresh warehouse. "Earlier" is posted first. "FirstTied" (created first, two lines) and "SecondTied" get the
    /// identical, newest PostedAt: only the tie-break decides their relative order.
    /// </summary>
    private async Task<Scenario> PostTwoDocumentsAtTheSameInstantAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();

        var earlier = await s.ReceiveAsync(warehouse.Id, new Line(a.Id, 1, 1m));

        var firstTied = await s.DraftReceiptAsync(s.Admin, warehouse.Id, new Line(a.Id, 2, 1m), new Line(b.Id, 3, 1m));
        var secondTied = await s.DraftReceiptAsync(s.Admin, warehouse.Id, new Line(a.Id, 5, 1m));

        await s.CompleteAsync(await s.SubmitAsync(s.Admin, firstTied));
        await s.CompleteAsync(await s.SubmitAsync(s.Admin, secondTied));
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET PostedAt = @at WHERE Id IN (@first, @second)",
            ("@at", DateTime.UtcNow.AddHours(1)), ("@first", firstTied.Id), ("@second", secondTied.Id));

        return new Scenario(warehouse.Id, earlier.No, firstTied.No, secondTied.No, a.Sku, b.Sku);
    }

    private async Task<List<(string Document, string Sku)>> PageAsync(int warehouseId, int page, int pageSize)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var result = (await admin.Reports.MovementsInWarehouseAsync(warehouseId, page, pageSize)).Data<PagedResult<MovementRow>>();
        return result.Items.Select(r => (r.DocNo, r.Sku)).ToList();
    }

    private async Task<List<(string Document, string Sku)>> WalkAsync(int warehouseId, int pageSize)
    {
        var rows = new List<(string Document, string Sku)>();
        for (var page = 1; page <= 20; page++)
        {
            var items = await PageAsync(warehouseId, page, pageSize);
            rows.AddRange(items);
            if (items.Count < pageSize) break;
        }
        return rows;
    }

    [Fact]
    public async Task Documents_posted_at_the_same_instant_come_newest_document_first_with_their_lines_in_entry_order_and_older_postings_last()
    {
        var x = await PostTwoDocumentsAtTheSameInstantAsync();

        var rows = await PageAsync(x.WarehouseId, 1, 50);

        // SecondTied has the higher document id, so it comes first; the lines of FirstTied keep their entry order (the old order, by line id, listed FirstTied first).
        Assert.Equal(
            [(x.SecondTied, x.SkuA), (x.FirstTied, x.SkuA), (x.FirstTied, x.SkuB), (x.Earlier, x.SkuA)],
            rows);
    }

    [Fact]
    public async Task The_order_does_not_depend_on_the_page_size_so_a_walk_never_skips_or_repeats_a_row()
    {
        var x = await PostTwoDocumentsAtTheSameInstantAsync();
        var whole = await PageAsync(x.WarehouseId, 1, 50);

        foreach (var pageSize in new[] { 1, 2, 3 })
        {
            var walked = await WalkAsync(x.WarehouseId, pageSize);
            Assert.Equal(whole, walked);
            Assert.Equal(walked.Count, walked.Distinct().Count());
        }
    }

    [Fact]
    public async Task The_total_counts_every_row_the_pages_return()
    {
        var x = await PostTwoDocumentsAtTheSameInstantAsync();
        var admin = await fixture.AsAsync(Roles.Admin);

        var page = (await admin.Reports.MovementsInWarehouseAsync(x.WarehouseId, pageSize: 2)).Data<PagedResult<MovementRow>>();

        Assert.Equal(4, page.Total);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task The_export_lists_the_rows_in_the_same_order_as_the_pages()
    {
        var x = await PostTwoDocumentsAtTheSameInstantAsync();
        var admin = await fixture.AsAsync(Roles.Admin);

        var content = Xlsx.Read((await admin.Reports.MovementsExportInWarehouseAsync(x.WarehouseId)).Bytes);

        var exported = content.Rows.Select(r => (content.Cell(r, "Document"), content.Cell(r, "SKU"))).ToList();
        Assert.Equal(await PageAsync(x.WarehouseId, 1, 50), exported);
    }

    [Fact]
    public async Task Repeated_requests_return_the_same_order()
    {
        var x = await PostTwoDocumentsAtTheSameInstantAsync();
        var first = await PageAsync(x.WarehouseId, 1, 50);

        for (var i = 0; i < 3; i++) Assert.Equal(first, await PageAsync(x.WarehouseId, 1, 50));
    }
}
