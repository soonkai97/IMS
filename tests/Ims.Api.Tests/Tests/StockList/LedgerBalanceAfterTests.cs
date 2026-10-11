using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.StockList;

/// <summary>PS-14: the product ledger says what the product held right after each movement, derived from the balance so the ledger itself stays append-only.</summary>
[Collection(ApiCollection.Name)]
public class LedgerBalanceAfterTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

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
    private async Task<(StockScenario S, int ProductId)> MovementsAsync()
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
        var byCursor = (await s.Admin.GetAsync($"/api/stock/ledger?productId={productId}&afterId={whole[1].Id}&pageSize=2")).Data<PagedResult<LedgerRow>>().Items;

        Assert.Equal(whole.Select(r => (r.Id, r.BalanceAfter)), walked.Select(r => (r.Id, r.BalanceAfter)));
        Assert.Equal(whole.Skip(2).Take(2).Select(r => (r.Id, r.BalanceAfter)), byCursor.Select(r => (r.Id, r.BalanceAfter)));
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
