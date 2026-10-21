using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Warehouses;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.6: <c>GET /api/purchase-orders/suggestions</c> on a real SQL Server. What to order for ONE warehouse: the low products there topped up to twice their safety level, less what is on hand and still on order (the
/// remaining quantity of the Draft, Submitted and Approved orders of that warehouse). The database is shared by all test classes, so every test works in a warehouse of its own and looks for its own products by SKU.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReorderSuggestionTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private WarehouseDto _w = null!;

    public async Task InitializeAsync()
    {
        _p = await CreateAsync(fixture);
        _w = await _p.NewWarehouseAsync();
    }

    public Task DisposeAsync() => ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private Task<ProductDto> LowProductAsync(decimal safety, decimal onHand, decimal unitCost = 2m) => LowProductAsync(_w.Id, safety, onHand, unitCost);

    private async Task<ProductDto> LowProductAsync(Guid warehouse, decimal safety, decimal onHand, decimal unitCost = 2m)
    {
        var product = await _p.NewProductAsync(b => b.WithSafetyStock(safety));
        if (onHand > 0) await _p.Stock.ReceiveAsync(warehouse, new Line(product.Id, onHand, unitCost));
        return product;
    }

    /// <summary>The rows of a warehouse for this test's products only (matched by SKU), as the caller sees them.</summary>
    private async Task<IReadOnlyList<ReorderSuggestion>> RowsAsync(ApiClient who, Guid warehouse, params ProductDto[] products)
    {
        var skus = products.Select(p => p.Sku).ToHashSet();
        var rows = new List<ReorderSuggestion>();
        for (var page = 1; ; page++)
        {
            var result = (await who.PurchaseOrders.SuggestionsAsync(warehouse, page: page, pageSize: 500)).Data<PagedResult<ReorderSuggestion>>();
            rows.AddRange(result.Items.Where(r => skus.Contains(r.Sku)));
            if (page * result.PageSize >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    private async Task<ReorderSuggestion?> RowAsync(ProductDto product, Guid? warehouse = null) => (await RowsAsync(_p.Admin, warehouse ?? _w.Id, product)).SingleOrDefault();

    /// <summary>A Draft in the test warehouse for the default supplier, then moved to <paramref name="status"/> (the row is written straight, so Closed and Cancelled need no workflow).</summary>
    private Task<PurchaseOrderDto> OrderAsync(PurchaseOrderStatus status, params (ProductDto Product, decimal Qty)[] lines) => OrderAsync(_w.Id, status, lines);

    private async Task<PurchaseOrderDto> OrderAsync(Guid warehouse, PurchaseOrderStatus status, params (ProductDto Product, decimal Qty)[] lines)
    {
        var builder = new PurchaseOrderBuilder(warehouse, fixture.Reference.SupplierId);
        foreach (var (product, qty) in lines) builder.WithLine(product.Id, qty, 1m);
        var order = (await _p.Admin.PurchaseOrders.CreateAsync(builder.Build())).Data<PurchaseOrderDto>();
        if (status != PurchaseOrderStatus.Draft) await _p.SetStatusAsync(order, status);
        return order;
    }

    private async Task PostReceiptAsync(PurchaseOrderDto order, int lineNo, decimal qty)
    {
        var receipt = (await _p.Admin.PurchaseOrders.CreateReceiptAsync(order.Id)).Data<DocumentDto>();
        var line = receipt.Lines.Single(l => l.PurchaseOrderLineNo == lineNo);
        var saved = (await _p.Admin.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(line.ProductId, qty, lineNo, uomId: line.UomId)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        await _p.PostAsync(saved, _p.Admin);
    }

    // ----- acceptance 1: the arithmetic -----
    [Fact]
    public async Task On_hand_3_safety_10_and_nothing_on_order_suggests_17()
    {
        var product = await LowProductAsync(safety: 10, onHand: 3);

        var row = await RowAsync(product);

        Assert.NotNull(row);
        Assert.Equal((product.Id, product.Sku, _w.Id, _w.Code), (row.ProductId, row.Sku, row.WarehouseId, row.WarehouseCode));
        Assert.Equal((3m, 10m, 0m, 17m), (row.QtyOnHand, row.SafetyStock, row.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task An_approved_line_of_10_takes_the_suggestion_to_7_and_17_or_more_on_order_leaves_the_row_out()
    {
        var product = await LowProductAsync(safety: 10, onHand: 3);
        await OrderAsync(PurchaseOrderStatus.Approved, (product, 10m));

        var seven = await RowAsync(product);
        await OrderAsync(PurchaseOrderStatus.Approved, (product, 7m));
        var covered = await RowAsync(product);

        Assert.Equal((10m, 7m), (seven!.OnOrderQty, seven.SuggestedQty));
        Assert.Null(covered);
    }

    [Fact]
    public async Task A_product_above_its_safety_level_is_not_listed_and_one_exactly_on_it_is()
    {
        var above = await LowProductAsync(safety: 10, onHand: 11);
        var on = await LowProductAsync(safety: 10, onHand: 10);

        Assert.Null(await RowAsync(above));
        Assert.Equal(10m, (await RowAsync(on))!.SuggestedQty); // 20 - 10
    }

    // ----- acceptance 2: what counts as on order -----
    [Fact]
    public async Task Draft_submitted_and_approved_orders_count_and_closed_and_cancelled_orders_do_not()
    {
        var product = await LowProductAsync(safety: 100, onHand: 10); // suggests 190 with nothing on order
        foreach (var status in new[] { PurchaseOrderStatus.Closed, PurchaseOrderStatus.Cancelled }) await OrderAsync(status, (product, 50m));
        var untouched = await RowAsync(product);
        Assert.Equal((0m, 190m), (untouched!.OnOrderQty, untouched.SuggestedQty));

        await OrderAsync(PurchaseOrderStatus.Draft, (product, 1m));
        await OrderAsync(PurchaseOrderStatus.Submitted, (product, 2m));
        await OrderAsync(PurchaseOrderStatus.Approved, (product, 4m));

        var row = await RowAsync(product);

        Assert.Equal((7m, 183m), (row!.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task What_was_received_against_an_approved_order_is_no_longer_on_order()
    {
        var product = await LowProductAsync(safety: 10, onHand: 0);
        var order = await OrderAsync(PurchaseOrderStatus.Approved, (product, 10m));
        await PostReceiptAsync(order, lineNo: 1, qty: 6m);

        var row = await RowAsync(product);

        // 6 received: on hand 6, 4 still to come, so 20 - 6 - 4
        Assert.Equal((6m, 4m, 10m), (row!.QtyOnHand, row.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task A_fully_received_line_counts_for_nothing()
    {
        var product = await LowProductAsync(safety: 10, onHand: 0);
        var order = await OrderAsync(PurchaseOrderStatus.Approved, (product, 4m));
        await PostReceiptAsync(order, lineNo: 1, qty: 4m);

        var row = await RowAsync(product);

        Assert.Equal((4m, 0m, 16m), (row!.QtyOnHand, row.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task The_same_product_on_two_lines_and_two_orders_is_added_up_with_each_line_taking_off_its_own_received_quantity()
    {
        var product = await LowProductAsync(safety: 50, onHand: 0);
        var other = await _p.NewProductAsync();
        var two = await OrderAsync(PurchaseOrderStatus.Approved, (product, 10m), (other, 5m), (product, 8m));
        await OrderAsync(PurchaseOrderStatus.Draft, (product, 3m));
        await PostReceiptAsync(two, lineNo: 3, qty: 5m); // 5 of the 8 on the second line of the product

        var row = await RowAsync(product);

        // on order: 10 + (8 - 5) + 3 = 16; on hand 5
        Assert.Equal((5m, 16m, 79m), (row!.QtyOnHand, row.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task Only_the_orders_of_the_asked_warehouse_count()
    {
        var elsewhere = await _p.NewWarehouseAsync();
        var product = await LowProductAsync(safety: 10, onHand: 3);
        await OrderAsync(elsewhere.Id, PurchaseOrderStatus.Approved, (product, 10m));

        Assert.Equal(0m, (await RowAsync(product))!.OnOrderQty);
    }

    // ----- acceptance 3: negative, never stocked, stocked elsewhere -----
    [Fact]
    public async Task A_negative_balance_with_no_safety_level_suggests_the_shortfall_less_what_is_on_order()
    {
        var home = fixture.Reference.DefaultWarehouseId; // the Clerk works there only; the override and the approval are the same as in the low-stock tests
        var product = await LowProductAsync(home, safety: 0, onHand: 5);
        await using (await SettingsOverride.ApplyAsync(_p.Admin, allowNegativeStock: true))
        {
            var submitted = await _p.Stock.SubmitAsync(_p.Clerk, await _p.Stock.DraftIssueAsync(_p.Clerk, home, new Line(product.Id, 9)));
            await _p.Stock.CompleteAsync(submitted, "Counted by hand, delivery is late");
        }
        var short4 = await RowAsync(product, home);
        Assert.Equal((-4m, 4m), (short4!.QtyOnHand, short4.SuggestedQty));

        await OrderAsync(home, PurchaseOrderStatus.Approved, (product, 3m));

        var row = await RowAsync(product, home);

        Assert.Equal((-4m, 3m, 1m), (row!.QtyOnHand, row.OnOrderQty, row.SuggestedQty));
    }

    [Fact]
    public async Task A_product_with_a_safety_level_that_was_never_stocked_is_listed_with_nothing_on_hand_and_no_value()
    {
        var product = await _p.NewProductAsync(b => b.WithSafetyStock(10));
        await OrderAsync(PurchaseOrderStatus.Approved, (product, 5m));

        var row = await RowAsync(product);

        Assert.Equal((_w.Id, 0m, 5m, 15m), (row!.WarehouseId, row.QtyOnHand, row.OnOrderQty, row.SuggestedQty));
        Assert.Null(row.SuggestedValue);
    }

    [Fact]
    public async Task A_product_stocked_only_in_another_warehouse_is_not_listed_here_and_one_without_a_safety_level_is_not_listed_unless_negative()
    {
        var elsewhere = await _p.NewWarehouseAsync();
        var stockedElsewhere = await LowProductAsync(elsewhere.Id, safety: 10, onHand: 30);
        var noSafety = await LowProductAsync(safety: 0, onHand: 3);

        Assert.Null(await RowAsync(stockedElsewhere));
        Assert.Null(await RowAsync(noSafety));
    }

    [Fact]
    public async Task A_closed_warehouse_and_an_inactive_product_list_nothing()
    {
        var product = await LowProductAsync(safety: 10, onHand: 1);
        var inactive = await _p.NewProductAsync(b => b.WithSafetyStock(10).Inactive());
        var closed = await _p.NewWarehouseAsync(isActive: false);

        Assert.Empty(await RowsAsync(_p.Admin, closed.Id, product, inactive));
        Assert.Null(await RowAsync(inactive));
        Assert.NotNull(await RowAsync(product));
    }

    // ----- acceptance 4: the gate, the permissions, the scope -----
    [Fact]
    public async Task While_the_module_is_off_the_endpoint_is_404_for_every_signed_in_caller_and_401_for_no_token()
    {
        await _p.TurnOffAsync();

        foreach (var who in new[] { _p.Admin, _p.Owner, _p.Supervisor, _p.Clerk, _p.Staff, Forged(PermissionCatalog.Codes), Forged([]) })
            (await who.PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal(401, (await fixture.Anonymous().PurchaseOrders.SuggestionsAsync(_w.Id)).StatusCode);
    }

    [Fact]
    public async Task A_forged_token_without_po_read_is_403_even_with_every_other_permission_and_the_role_claim_of_an_administrator()
    {
        (await Forged(PermissionCatalog.Codes.Except([Permissions.PoRead])).PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Forged([Permissions.PoManage]).PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Forged([Permissions.PoRead]).PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeOk();
    }

    [Fact]
    public async Task Every_default_role_that_reads_orders_reads_the_suggestions()
    {
        foreach (var who in new[] { _p.Admin, _p.Owner, _p.Supervisor, _p.Clerk, _p.Staff })
            (await who.PurchaseOrders.SuggestionsAsync(fixture.Reference.DefaultWarehouseId)).ShouldBeOk();
    }

    [Fact]
    public async Task A_missing_warehouse_is_a_400_and_an_unknown_one_a_404()
    {
        (await _p.Admin.PurchaseOrders.SuggestionsAsync(null)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.PurchaseOrders.SuggestionsAsync(Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_caller_scoped_to_one_warehouse_gets_its_suggestions_and_another_warehouse_is_404()
    {
        var other = await _p.NewWarehouseAsync();
        var product = await LowProductAsync(safety: 10, onHand: 3);
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [_w.Id]);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(account);

        Assert.NotNull((await RowsAsync(clerk, _w.Id, product)).SingleOrDefault());
        (await clerk.PurchaseOrders.SuggestionsAsync(other.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_scoped_caller_is_told_nothing_of_stock_in_a_warehouse_it_cannot_read_so_the_product_looks_never_stocked()
    {
        var hidden = await _p.NewWarehouseAsync();
        var product = await LowProductAsync(hidden.Id, safety: 10, onHand: 30); // plenty, but where the clerk cannot see
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [_w.Id]);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(account);

        var row = (await RowsAsync(clerk, _w.Id, product)).Single();

        Assert.Equal((0m, 20m), (row.QtyOnHand, row.SuggestedQty)); // the low-stock rule: only the caller's warehouses count
        Assert.Null(await RowAsync(product)); // the administrator sees the truth: it is stocked
    }

    // ----- cost visibility -----
    [Fact]
    public async Task The_suggested_value_is_the_quantity_at_the_average_cost_with_cost_view_and_null_in_the_raw_json_without_it()
    {
        var home = fixture.Reference.DefaultWarehouseId; // the default roles other than the Administrator work there only
        var product = await LowProductAsync(home, safety: 10, onHand: 3, unitCost: 2.5m); // suggests 17 at 2.50

        Assert.Equal(42.5m, (await RowAsync(product, home))!.SuggestedValue);
        foreach (var who in new[] { _p.Owner, _p.Supervisor, _p.Clerk })
            Assert.Equal(42.5m, (await RowsAsync(who, home, product)).Single().SuggestedValue);

        var json = (await _p.Staff.PurchaseOrders.SuggestionsAsync(home, search: product.Sku)).ShouldBeOk().Body;
        var row = JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, row.GetProperty("suggestedValue").ValueKind);
        Assert.Equal(17m, row.GetProperty("suggestedQty").GetDecimal()); // what is not money is still there
    }

    // ----- paging and search -----
    [Fact]
    public async Task Rows_are_paged_in_sku_order_with_the_total_and_no_row_repeats_or_goes_missing()
    {
        var prefix = Unique.Code("RO");
        foreach (var letter in new[] { "C", "A", "B" }) await _p.NewProductAsync(b => b.WithSku($"{prefix}-{letter}").WithSafetyStock(10));

        var first = (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: prefix, page: 1, pageSize: 2)).Data<PagedResult<ReorderSuggestion>>();
        var second = (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: prefix, page: 2, pageSize: 2)).Data<PagedResult<ReorderSuggestion>>();

        Assert.Equal(3, first.Total);
        Assert.Equal([$"{prefix}-A", $"{prefix}-B"], first.Items.Select(r => r.Sku));
        Assert.Equal([$"{prefix}-C"], second.Items.Select(r => r.Sku));
        Assert.Equal((1, 2), (first.Page, first.PageSize));
    }

    [Fact]
    public async Task The_search_matches_the_sku_or_the_name_and_a_search_that_matches_nothing_is_an_empty_page()
    {
        var product = await LowProductAsync(safety: 10, onHand: 1);

        var bySku = (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: product.Sku)).Data<PagedResult<ReorderSuggestion>>();
        var byName = (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: product.Name)).Data<PagedResult<ReorderSuggestion>>();
        var none = (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: "no-such-product-" + Guid.NewGuid().ToString("N"))).Data<PagedResult<ReorderSuggestion>>();

        Assert.Equal(product.Id, bySku.Items.Single().ProductId);
        Assert.Contains(byName.Items, r => r.ProductId == product.Id);
        Assert.Empty(none.Items);
        Assert.Equal(0, none.Total);
    }

    [Fact]
    public async Task A_page_beyond_the_offset_limit_is_a_400_not_a_deep_scan()
    {
        (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, page: 100, pageSize: 500)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_search_of_one_or_two_letters_is_a_400()
    {
        (await _p.Admin.PurchaseOrders.SuggestionsAsync(_w.Id, search: "ab")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    // ----- statements -----
    private const int ModuleGate = 1, WarehouseLookup = 1, Count = 1, Page = 1; // the on-order sum is a join inside the count and the page, not a command of its own

    [Fact]
    public async Task A_page_costs_four_commands_the_module_gate_the_warehouse_lookup_the_count_and_the_page_whatever_is_on_order()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var product = await LowProductAsync(safety: 10, onHand: 3);
        var other = await LowProductAsync(safety: 10, onHand: 4);
        await OrderAsync(PurchaseOrderStatus.Approved, (product, 2m), (other, 2m));

        (await client.PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeOk(); // the first request of a host does work once
        log.Clear();
        (await client.PurchaseOrders.SuggestionsAsync(_w.Id)).ShouldBeOk();

        Assert.True(log.Count == ModuleGate + WarehouseLookup + Count + Page, string.Join(" ---- ", log.Texts));
    }
}
