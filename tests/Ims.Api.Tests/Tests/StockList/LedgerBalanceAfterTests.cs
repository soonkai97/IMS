using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>PS-14: the product ledger says what the product held right after each movement. The number is stored on the row when it is written (so a deep page costs the same as the first), and the ledger stays append-only.</summary>
[Collection(ApiCollection.Name)]
public class LedgerBalanceAfterTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private static async Task<List<LedgerRow>> RowsAsync(ApiClient who, string query)
    {
        var rows = new List<LedgerRow>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/ledger?{query}&page={page}&pageSize=500")).Data<PagedResult<LedgerRow>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    /// <summary>10, then +5 = 15, then -7 = 8, then +3 = 11, then -2 = 9.</summary>
    private async Task<(StockScenario S, Guid ProductId)> MovementsAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 7));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 2m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 2));
        return (s, product.Id);
    }

    [Fact]
    public async Task Each_row_shows_the_balance_right_after_it_newest_first()
    {
        var (s, productId) = await MovementsAsync();

        var rows = await RowsAsync(s.Admin, $"productId={productId}");

        Assert.Equal([-2m, 3m, -7m, 5m, 10m], rows.Select(r => r.QtyDelta));
        Assert.Equal([9m, 11m, 8m, 15m, 10m], rows.Select(r => r.BalanceAfter));
        Assert.Equal(await s.OnHandAsync(productId, Warehouse), rows[0].BalanceAfter); // the newest row is the balance now
    }

    [Fact]
    public async Task The_same_numbers_come_out_whatever_the_page_size_and_the_page()
    {
        var (s, productId) = await MovementsAsync();
        var whole = await RowsAsync(s.Admin, $"productId={productId}");

        var walked = new List<LedgerRow>();
        for (var page = 1; walked.Count < whole.Count; page++)
            walked.AddRange((await s.Admin.GetAsync($"/api/stock/ledger?productId={productId}&page={page}&pageSize=2")).Data<PagedResult<LedgerRow>>().Items);
        var firstPage = (await s.Admin.GetAsync($"/api/stock/ledger?productId={productId}&pageSize=2")).Data<PagedResult<LedgerRow>>();   // its nextCursor points after whole[1]
        var byCursor = (await s.Admin.GetAsync($"/api/stock/ledger?productId={productId}&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}&pageSize=2")).Data<PagedResult<LedgerRow>>().Items;

        Assert.Equal(whole.Select(r => (r.SourceDocNo, r.Reason, r.QtyDelta, r.BalanceAfter)), walked.Select(r => (r.SourceDocNo, r.Reason, r.QtyDelta, r.BalanceAfter)));
        Assert.Equal(whole.Skip(2).Take(2).Select(r => (r.SourceDocNo, r.Reason, r.QtyDelta, r.BalanceAfter)), byCursor.Select(r => (r.SourceDocNo, r.Reason, r.QtyDelta, r.BalanceAfter)));
    }

    [Fact]
    public async Task A_reversal_is_a_movement_like_any_other()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        var mistake = await s.ReceiveAsync(Warehouse, new Line(product.Id, 4, 2m));
        (await s.Supervisor.Documents.ReverseAsync(mistake.Id, "Entered twice")).ShouldBeOk();

        var rows = await RowsAsync(s.Admin, $"productId={product.Id}");

        Assert.Equal([LedgerReason.Reversal, LedgerReason.Receipt, LedgerReason.Receipt], rows.Select(r => r.Reason));
        Assert.Equal([10m, 14m, 10m], rows.Select(r => r.BalanceAfter));
    }

    [Fact]
    public async Task Each_warehouse_has_its_own_running_balance_and_a_warehouse_filter_shows_one_of_them()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        await s.ReceiveAsync(other.Id, new Line(product.Id, 6, 2m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 3));
        await s.ReceiveAsync(other.Id, new Line(product.Id, 1, 2m));

        var both = await RowsAsync(s.Admin, $"productId={product.Id}");
        var justOther = await RowsAsync(s.Admin, $"productId={product.Id}&warehouseId={other.Id}");

        // Newest first: the other warehouse +1 (7 there), WH01 -3 (7 there), the other +6 (6), WH01 +10 (10).
        Assert.Equal([1m, -3m, 6m, 10m], both.Select(r => r.QtyDelta));
        Assert.Equal([7m, 7m, 6m, 10m], both.Select(r => r.BalanceAfter));
        Assert.Equal([7m, 6m], justOther.Select(r => r.BalanceAfter));
    }

    [Fact]
    public async Task The_rows_of_one_document_still_show_the_balance_after_each_of_them()
    {
        var (s, productId) = await MovementsAsync();
        var all = await RowsAsync(s.Admin, $"productId={productId}");
        var issue = all.Single(r => r.QtyDelta == -7m);

        var rows = await RowsAsync(s.Admin, $"productId={productId}&sourceDocId={issue.SourceDocId}");

        Assert.Equal((-7m, 8m), (rows.Single().QtyDelta, rows.Single().BalanceAfter!.Value));
    }

    [Fact]
    public async Task Without_a_product_there_is_no_single_running_balance()
    {
        var (s, productId) = await MovementsAsync();

        var rows = await RowsAsync(s.Admin, $"warehouseId={Warehouse}");

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Null(r.BalanceAfter));
        Assert.NotEmpty(await RowsAsync(s.Admin, $"productId={productId}"));
    }

    [Fact]
    public async Task A_user_gets_the_balance_of_their_own_warehouses_only()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        await s.ReceiveAsync(other.Id, new Line(product.Id, 99, 2m));

        var theirs = await RowsAsync(s.Staff, $"productId={product.Id}");

        var row = Assert.Single(theirs);
        Assert.Equal((10m, 10m), (row.QtyDelta, row.BalanceAfter!.Value));
    }

    /// <summary>Rows whose stored balance is not the running sum of their (product, warehouse) up to and including them, in ledger order.</summary>
    private const string MismatchSql = @"
SELECT COUNT(*) FROM (
    SELECT l.BalanceAfter, SUM(l.QtyDelta) OVER (PARTITION BY l.ProductId, l.WarehouseId ORDER BY l.Id ROWS UNBOUNDED PRECEDING) AS Running
    FROM StockLedgerEntry l {0}) x
WHERE x.BalanceAfter <> x.Running";

    [Fact]
    public async Task The_stored_balance_is_the_running_sum_for_receipts_issues_reversals_and_two_lines_of_one_product()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m), new Line(product.Id, 4, 3m)); // two lines of the same product in one document
        var issue = await s.IssueAsync(Warehouse, new Line(product.Id, 6));
        (await s.Supervisor.Documents.ReverseAsync(issue.Id, "Wrong customer")).ShouldBeOk();

        var rows = await RowsAsync(s.Admin, $"productId={product.Id}");

        Assert.Equal([14m, 8m, 14m, 10m], rows.Select(r => r.BalanceAfter!.Value)); // newest first: the reversal 14, the issue 8, the second receipt line 14, the first 10
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>(string.Format(MismatchSql, "WHERE l.ProductId = (SELECT Id FROM Product WHERE PublicId = @p)"), ("@p", product.Id)));
        Assert.Equal(await s.OnHandAsync(product.Id, Warehouse), rows[0].BalanceAfter);
    }

    [Fact]
    public async Task Postings_made_at_the_same_instant_never_leave_a_balance_that_another_posting_overtook()
    {
        // The stored number is written under the (warehouse, product) lock with the balance it follows, so a posting cannot slip in between a read and a write
        // (the old derived value ran three statements and could be one posting off).
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1000, 2m));
        var drafts = new List<DocumentDto>();
        for (var i = 0; i < 24; i++)
            drafts.Add(await s.SubmitAsync(s.Admin, i % 3 == 0
                ? await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 5, 2m))
                : await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 3))));

        var responses = await Support.Concurrently.RunAsync(drafts.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        Assert.All(responses, r => Assert.True(r.IsSuccess, $"{(int)r.Status} {r.Body}"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>(string.Format(MismatchSql, "WHERE l.ProductId = (SELECT Id FROM Product WHERE PublicId = @p)"), ("@p", product.Id)));
        var newest = (await RowsAsync(s.Admin, $"productId={product.Id}")).First();
        Assert.Equal(await s.OnHandAsync(product.Id, Warehouse), newest.BalanceAfter);
    }

    [Fact]
    public async Task Every_ledger_row_of_the_database_carries_the_running_balance()
    {
        await MovementsAsync(); // make sure there is something, then look at everything every other test wrote too

        Assert.Equal(0, await fixture.Database.ScalarAsync<int>(string.Format(MismatchSql, "")));
    }

    [Fact]
    public async Task The_balance_after_the_newest_row_always_equals_the_stock_row()
    {
        var (s, productId) = await MovementsAsync();

        var newest = (await RowsAsync(s.Admin, $"productId={productId}")).First();
        var balance = await s.BalanceAsync(productId, Warehouse);

        Assert.Equal(balance!.QtyOnHand, newest.BalanceAfter);
        Assert.Equal(await s.LedgerSumAsync(productId, Warehouse), newest.BalanceAfter);
    }
}
