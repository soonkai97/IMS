using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Paging;

/// <summary>Every list answers with the same paged shape, clamps odd paging input instead of failing, and the big lists can be walked by cursor.</summary>
[Collection(ApiCollection.Name)]
public class PagingContractTests(ApiFixture fixture)
{
    public static IEnumerable<object[]> Lists() =>
    [
        ["/api/products"], ["/api/partners"], ["/api/users"], ["/api/documents"], ["/api/stock"],
        ["/api/stock/ledger"], ["/api/stock/low-stock"], ["/api/reports/movements"], ["/api/audit"]
    ];

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        return (await admin.GetAsync(url)).ShouldBeOk().Json();
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task A_list_answers_with_items_total_page_pagesize_and_nextcursor(string url)
    {
        using var json = await GetJsonAsync(url + "?pageSize=3");
        var root = json.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
        Assert.True(root.GetProperty("total").GetInt32() >= root.GetProperty("items").GetArrayLength());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(3, root.GetProperty("pageSize").GetInt32());
        Assert.True(root.TryGetProperty("nextCursor", out _), "nextCursor must be present (null when there is no further page)");
        Assert.True(root.GetProperty("items").GetArrayLength() <= 3);
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Page_zero_negative_and_page_size_zero_or_huge_are_clamped_not_errors(string url)
    {
        foreach (var query in new[] { "page=0", "page=-5", "pageSize=0", "pageSize=-1", "pageSize=9999", $"pageSize={int.MaxValue}" })
        {
            using var json = await GetJsonAsync($"{url}?{query}");
            var root = json.RootElement;
            Assert.InRange(root.GetProperty("page").GetInt32(), 1, 100_000);
            Assert.InRange(root.GetProperty("pageSize").GetInt32(), 1, 500);
            Assert.True(root.GetProperty("items").GetArrayLength() <= root.GetProperty("pageSize").GetInt32(), $"{url}?{query}");
        }
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/documents")]
    [InlineData("/api/stock/ledger")]
    public async Task A_page_size_of_zero_returns_one_row_and_a_huge_one_is_capped(string url)
    {
        // The lists must hold at least one row: a product, a document and a ledger entry.
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        using var one = await GetJsonAsync(url + "?pageSize=0");
        using var huge = await GetJsonAsync(url + "?pageSize=9999");

        Assert.Equal(1, one.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, one.RootElement.GetProperty("items").GetArrayLength());
        Assert.InRange(huge.RootElement.GetProperty("pageSize").GetInt32(), 50, 500);
    }

    [Fact]
    public async Task A_page_past_the_end_but_inside_the_offset_cap_is_an_empty_list_with_the_true_total()
    {
        // page 1000 x 10 rows starts at row 9,990: below the 10,000-row OFFSET cap, past the end of a small table.
        using var json = await GetJsonAsync("/api/products?page=1000&pageSize=10");

        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(json.RootElement.GetProperty("total").GetInt32() > 0);
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task A_page_beyond_the_offset_cap_is_a_400_that_points_to_cursor_paging(string url)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var problem = (await admin.GetAsync($"{url}?page={int.MaxValue}")).ShouldBeProblem(System.Net.HttpStatusCode.BadRequest);

        Assert.Contains("cursor", problem.Detail);
    }

    // ---------- cursor (seek) paging ----------

    [Fact]
    public async Task The_ledger_can_be_walked_by_cursor_without_gaps_or_repeats()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        for (var i = 0; i < 5; i++) await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        var ids = new List<long>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"))).Data<PagedResult<LedgerRow>>();
            ids.AddRange(page.Items.Select(r => r.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages); // 2 + 2 + 1
        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Distinct().Order(), ids.Order());
        Assert.Equal(ids.OrderByDescending(i => i), ids); // newest first, strictly decreasing
    }

    [Fact]
    public async Task The_next_cursor_continues_after_the_last_row_of_a_full_page_hides_its_id_and_is_absent_on_a_short_page()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        for (var i = 0; i < 3; i++) await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        var whole = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=10")).Data<PagedResult<LedgerRow>>();
        var full = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=2")).Data<PagedResult<LedgerRow>>();
        var rest = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=2&cursor={Uri.EscapeDataString(full.NextCursor!)}")).Data<PagedResult<LedgerRow>>();

        Assert.NotNull(full.NextCursor);
        Assert.DoesNotContain(full.Items[^1].Id.ToString(), full.NextCursor); // the key is not in plain sight
        Assert.Equal(whole.Items.Skip(2).Select(r => r.Id), rest.Items.Select(r => r.Id));
        Assert.Null(whole.NextCursor);
    }

    [Fact]
    public async Task A_cursor_of_one_list_is_a_400_on_another()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        for (var i = 0; i < 3; i++) await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var ledgerCursor = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&pageSize=2")).Data<PagedResult<LedgerRow>>().NextCursor!;

        foreach (var url in new[] { "/api/documents", "/api/audit" })
            Assert.Equal([Messages.CursorInvalid], (await s.Admin.GetAsync($"{url}?pageSize=2&cursor={Uri.EscapeDataString(ledgerCursor)}")).ShouldBeProblem(System.Net.HttpStatusCode.BadRequest).ErrorsFor("cursor"));
    }

    [Theory]
    [InlineData("12345")]            // the old integer cursor is not a cursor any more
    [InlineData("not-a-cursor!!")]
    [InlineData("AQAA")]             // truncated
    public async Task A_cursor_the_server_did_not_make_is_a_400_on_the_cursor_field_never_a_500(string cursor)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        foreach (var url in new[] { "/api/stock/ledger", "/api/documents", "/api/audit" })
        {
            var problem = (await admin.GetAsync($"{url}?pageSize=2&cursor={cursor}")).ShouldBeProblem(System.Net.HttpStatusCode.BadRequest);
            Assert.Equal([Messages.CursorInvalid], problem.ErrorsFor("cursor"));
        }
    }

    [Fact]
    public async Task Include_total_false_skips_the_count()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        var ledger = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}&includeTotal=false")).Data<PagedResult<LedgerRow>>();
        var documents = (await s.Admin.GetAsync("/api/documents?includeTotal=false&pageSize=5")).Data<PagedResult<DocumentListItem>>();
        var audit = (await s.Admin.GetAsync("/api/audit?includeTotal=false&pageSize=5")).Data<PagedResult<AuditEntry>>();
        var counted = (await s.Admin.GetAsync($"/api/stock/ledger?productId={product.Id}")).Data<PagedResult<LedgerRow>>();

        Assert.Equal(Ims.Application.Paging.TotalNotCounted, ledger.Total);
        Assert.Single(ledger.Items);
        Assert.Equal(-1, documents.Total);
        Assert.NotEmpty(documents.Items);
        Assert.Equal(-1, audit.Total);
        Assert.Equal(1, counted.Total);
    }

    [Fact]
    public async Task Documents_can_be_walked_by_cursor_inside_one_warehouse()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        for (var i = 0; i < 5; i++) await s.DraftReceiptAsync(s.Admin, warehouse.Id, new Line(product.Id, 1, 1m));

        var ids = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/documents?warehouseId={warehouse.Id}&pageSize=2&includeTotal=false" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await s.Admin.GetAsync(url)).Data<PagedResult<DocumentListItem>>();
            ids.AddRange(page.Items.Select(d => d.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }

    [Fact]
    public async Task The_audit_log_can_be_walked_by_cursor()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Admin);
        var (client, _) = await accounts.SignInAsync(account);
        for (var i = 0; i < 4; i++) await new ProductBuilder(fixture.Reference).CreateAsync(client);

        var seen = new List<long>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/audit?actor={Uri.EscapeDataString(account.Username)}&entityType=Product&pageSize=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await client.GetAsync(url)).Data<PagedResult<AuditEntry>>();
            seen.AddRange(page.Items.Select(e => e.Id));
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(4, seen.Count);
        Assert.Equal(seen.OrderByDescending(i => i), seen);
    }

    // ---------- sorting ----------

    [Fact]
    public async Task Products_sort_by_sku_or_name_ascending_or_descending()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var tag = Unique.Sku("SRT");
        // SKU order is the reverse of name order, so the two sorts cannot be confused.
        await new ProductBuilder(fixture.Reference).WithSku(tag + "-A").WithName("Zulu " + tag).CreateAsync(s.Admin);
        await new ProductBuilder(fixture.Reference).WithSku(tag + "-B").WithName("Mike " + tag).CreateAsync(s.Admin);
        await new ProductBuilder(fixture.Reference).WithSku(tag + "-C").WithName("Alpha " + tag).CreateAsync(s.Admin);

        async Task<string[]> SkusAsync(string sort) =>
            (await s.Admin.GetAsync($"/api/products?search={Uri.EscapeDataString(tag)}&{sort}")).Data<PagedResult<ProductDto>>().Items.Select(p => p.Sku[^1..]).ToArray();

        Assert.Equal(["A", "B", "C"], await SkusAsync("sortBy=Sku"));
        Assert.Equal(["C", "B", "A"], await SkusAsync("sortBy=Sku&desc=true"));
        Assert.Equal(["C", "B", "A"], await SkusAsync("sortBy=Name"));
        Assert.Equal(["A", "B", "C"], await SkusAsync("sortBy=Name&desc=true"));
    }

    [Fact]
    public async Task Stock_sorts_by_quantity_and_by_value()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var low = await s.NewProductAsync();
        var mid = await s.NewProductAsync();
        var high = await s.NewProductAsync();
        // quantity: low < mid < high; value (qty x cost): mid < high < low
        await s.ReceiveAsync(warehouse.Id, new Line(low.Id, 5, 1000m), new Line(mid.Id, 20, 1m), new Line(high.Id, 50, 2m));

        async Task<int[]> ProductsAsync(string sort) =>
            (await s.Admin.GetAsync($"/api/stock?warehouseId={warehouse.Id}&{sort}")).Data<PagedResult<StockRow>>().Items.Select(r => r.ProductId).ToArray();

        Assert.Equal([low.Id, mid.Id, high.Id], await ProductsAsync("sortBy=QtyOnHand"));
        Assert.Equal([high.Id, mid.Id, low.Id], await ProductsAsync("sortBy=QtyOnHand&desc=true"));
        Assert.Equal([mid.Id, high.Id, low.Id], await ProductsAsync("sortBy=Value"));
        Assert.Equal([low.Id, high.Id, mid.Id], await ProductsAsync("sortBy=Value&desc=true"));
    }

    [Fact]
    public async Task An_unknown_sort_column_is_400_not_500()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var response = await admin.GetAsync("/api/products?sortBy=NoSuchColumn");

        Assert.True(response.StatusCode is >= 400 and < 500, $"Got {response.StatusCode}: {response.Body}");
    }

    // ---------- filters that moved into paged lists ----------

    [Fact]
    public async Task Partners_and_users_can_be_searched()
    {
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk);
        var admin = await fixture.AsAsync(Roles.Admin);

        var users = (await admin.GetAsync($"/api/users?search={Uri.EscapeDataString(account.Username)}")).Data<PagedResult<Ims.Application.Features.Users.UserDto>>();
        var clerks = (await admin.GetAsync($"/api/users?role=Clerk&search={Uri.EscapeDataString(account.Username)}")).Data<PagedResult<Ims.Application.Features.Users.UserDto>>();
        var admins = (await admin.GetAsync($"/api/users?role=Admin&search={Uri.EscapeDataString(account.Username)}")).Data<PagedResult<Ims.Application.Features.Users.UserDto>>();

        Assert.Single(users.Items);
        Assert.Single(clerks.Items);
        Assert.Empty(admins.Items);
    }
}
