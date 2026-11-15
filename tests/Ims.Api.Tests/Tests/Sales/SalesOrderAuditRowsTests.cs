using System.Globalization;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// DDD-11-0, a characterization: the audit rows every sales-order verb writes TODAY, pinned before the dispatcher (DDD-11a) and the audit subscriber (DDD-11b) change how they are written. Nothing here
/// states a wish; it states what <c>main</c> does, and the rows must stay byte for byte the same after 11b.
/// <para>
/// What is compared: the whole trail of one order, read straight from the <c>AuditLogs</c> table in the order of its <c>Id</c>, as (actor, action, entity type, entity id, before, after) with the
/// <c>Before</c> and <c>After</c> columns as RAW strings (key order, enum names, nulls and decimal scale included: 12, 12.5 and 12.50 are three different strings). The expected strings are written out in
/// full with the real internal ids put in, so nothing is normalised except two generated values: the row <c>Id</c> (only its ORDER is used) and <c>At</c> (only checked to be a recent UTC time).
/// The order number, the actor names and the internal warehouse, customer, product and unit ids are exact.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderAuditRowsTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private StockScenario Stock => _s.Stock;

    private static readonly string Clerk = TestUsers.UsernameOf(Roles.Clerk);
    private static readonly string Supervisor = TestUsers.UsernameOf(Roles.Supervisor);
    private static readonly string Owner = TestUsers.UsernameOf(Roles.Owner);
    private static readonly string Admin = TestUsers.UsernameOf(Roles.Admin);

    // ----- reading the rows -----
    private sealed record Row(long Id, string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);

    /// <summary>The (actor, action, entity type, entity id, before, after) of a row: everything pinned except the generated Id and At.</summary>
    private sealed record Pinned(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After);

    private static Pinned Expect(string actor, string action, string no, string? before, string? after) => new(actor, action, AuditEntities.SalesOrder, no, before, after);

    private async Task<List<Row>> RowsAsync(string entityType, string entityId)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Actor, Action, EntityType, EntityId, [Before], [After], [At] FROM AuditLogs WHERE EntityType = @t AND EntityId = @id ORDER BY Id";
        command.Parameters.AddWithValue("@t", entityType);
        command.Parameters.AddWithValue("@id", entityId);
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Row(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7)));
        return rows;
    }

    /// <summary>The trail of one order: its rows, oldest first (by Id), each written moments ago.</summary>
    private async Task<List<Pinned>> TrailAsync(SalesOrderDto order)
    {
        var rows = await RowsAsync(AuditEntities.SalesOrder, order.No);
        Assert.All(rows, r => Assert.True(Math.Abs((DateTime.UtcNow - r.At).TotalMinutes) < 10, $"At {r.At:o} is not a recent UTC time"));
        return rows.Select(r => new Pinned(r.Actor, r.Action, r.EntityType, r.EntityId, r.Before, r.After)).ToList();
    }

    // ----- the ids the payloads carry (internal, generated: read back, not normalised) -----
    private async Task<(int Warehouse, int Customer)> HeaderIdsAsync(SalesOrderDto order)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT WarehouseId, CustomerId FROM SalesOrder WHERE PublicId = @id";
        command.Parameters.AddWithValue("@id", order.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>The internal product and unit ids of one line, as the price-change payload carries them.</summary>
    private async Task<(int Product, int Uom)> LineIdsAsync(SalesOrderDto order, int lineNo)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT l.ProductId, l.UomId FROM SalesOrderLine l JOIN SalesOrder o ON o.Id = l.SalesOrderId WHERE o.PublicId = @id AND l.[LineNo] = @n";
        command.Parameters.AddWithValue("@id", order.Id);
        command.Parameters.AddWithValue("@n", lineNo);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    // ----- the payload shapes, written out in full (raw JSON, key order as written by the handlers) -----
    private static string Summary(string status, (int Warehouse, int Customer) ids, int lines, string amount) =>
        $$"""{"Status":"{{status}}","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":{{lines}},"SellingAmount":{{amount}}}""";

    private static string StatusOnly(string status) => $$"""{"Status":"{{status}}"}""";

    private static decimal D(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture); // keeps the scale the text has: "12.50" is 12.50, not 12.5

    // ----- making orders -----
    private async Task<SalesOrderDto> CreateAsync(ApiClient by, decimal qty, decimal price, Guid? customer = null, Guid? product = null)
    {
        var id = product ?? (await _s.NewProductAsync()).Id;
        return (await by.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, customer ?? fixture.Reference.CustomerId).WithLine(id, qty, price).Build())).Data<SalesOrderDto>();
    }

    private static async Task<SalesOrderDto> SubmitAsync(ApiClient by, SalesOrderDto order) => (await by.SalesOrders.SubmitAsync(order.Id, order.ConcurrencyToken)).Data<SalesOrderDto>();

    private static async Task<SalesOrderDto> ApproveAsync(ApiClient by, SalesOrderDto order) => (await by.SalesOrders.ApproveAsync(order.Id, order.ConcurrencyToken)).Data<SalesOrderDto>();

    // ===== create =====
    [Theory]
    [InlineData("1", "12", "12")]
    [InlineData("1", "12.5", "12.5")]
    [InlineData("1", "12.50", "12.50")]
    [InlineData("10", "2.50", "25.00")]
    [InlineData("1.5", "12.5", "18.75")]
    public async Task Create_writes_one_row_with_the_status_the_header_ids_the_line_count_and_the_amount_at_the_scale_it_was_computed_in_memory(string qty, string price, string amountAsWritten)
    {
        var order = await CreateAsync(_s.Clerk, D(qty), D(price));

        var ids = await HeaderIdsAsync(order);
        Assert.Equal([Expect(Clerk, "sales-order.created", order.No, null, Summary("Draft", ids, 1, amountAsWritten))], await TrailAsync(order));
    }

    // ===== update =====
    [Fact]
    public async Task Update_writes_one_row_whose_before_is_read_back_from_the_database_at_two_decimals_and_whose_after_is_the_new_amount_as_computed_in_memory()
    {
        var product = await _s.NewProductAsync();
        var order = await CreateAsync(_s.Clerk, 10m, 2.50m, product: product.Id);
        var ids = await HeaderIdsAsync(order);
        var customer = fixture.Reference.CustomerId;

        var first = (await _s.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_s.Warehouse, customer).WithLine(product.Id, 1m, 12m).Build(), order.ConcurrencyToken)).Data<SalesOrderDto>();
        var second = (await _s.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_s.Warehouse, customer).WithLine(product.Id, 1m, 12.5m).WithLine(product.Id, 2m, 1.50m).Build(), first.ConcurrencyToken)).Data<SalesOrderDto>();
        _ = (await _s.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_s.Warehouse, customer).WithRemark("r").WithLine(product.Id, 3m, 4m).Build(), second.ConcurrencyToken)).ShouldBeOk();

        Assert.Equal(
            [
                Expect(Clerk, "sales-order.created", order.No, null, Summary("Draft", ids, 1, "25.00")),
                Expect(Clerk, "sales-order.updated", order.No, Summary("Draft", ids, 1, "25.00"), Summary("Draft", ids, 1, "12")),        // before: the stored 25.00; after: 1 x 12 as computed
                Expect(Clerk, "sales-order.updated", order.No, Summary("Draft", ids, 1, "12.00"), Summary("Draft", ids, 2, "15.50")),    // before: the stored amount reloaded, "12" has become "12.00"
                Expect(Clerk, "sales-order.updated", order.No, Summary("Draft", ids, 2, "15.50"), Summary("Draft", ids, 1, "12"))         // 3 x 4 = 12, again without a scale
            ],
            await TrailAsync(order));
    }

    // ===== price change: Created or Updated first, then PriceChanged =====
    private async Task<(Application.Features.Partners.PartnerDto Customer, ProductDto Product)> ListedAsync(decimal listPrice)
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().ForCustomer(customer.Id).WithItem(product.Id, listPrice));
        return (customer, product);
    }

    private static string PriceChanged(int line, (int Product, int Uom) ids, string source, string listed, string entered) =>
        $$"""{"Line":{{line}},"ProductId":{{ids.Product}},"UomId":{{ids.Uom}},"Source":"{{source}}","ListedPrice":{{listed}},"EnteredPrice":{{entered}}}""";

    [Fact]
    public async Task Create_with_a_price_that_differs_from_the_list_writes_Created_then_PriceChanged_and_the_audit_list_shows_them_newest_first()
    {
        var (customer, product) = await ListedAsync(5m);

        var order = await CreateAsync(_s.Clerk, 1m, 4.25m, customer.Id, product.Id);

        var ids = await HeaderIdsAsync(order);
        var line = await LineIdsAsync(order, 1);
        Assert.Equal(
            [
                Expect(Clerk, "sales-order.created", order.No, null, Summary("Draft", ids, 1, "4.25")),
                Expect(Clerk, "sales-order.price-changed", order.No, null, PriceChanged(1, line, "Customer", "5.00", "4.25"))
            ],
            await TrailAsync(order));
        await AssertListedNewestFirstAsync(order, "sales-order.price-changed", "sales-order.created");
    }

    [Fact]
    public async Task Update_with_a_repriced_line_writes_Updated_then_PriceChanged_and_the_audit_list_shows_them_newest_first()
    {
        var (customer, product) = await ListedAsync(5m);
        var order = await CreateAsync(_s.Clerk, 1m, 5m, customer.Id, product.Id);   // at the list price: no price row
        var ids = await HeaderIdsAsync(order);

        _ = (await _s.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_s.Warehouse, customer.Id).WithLine(product.Id, 1m, 4.5m).Build(), order.ConcurrencyToken)).ShouldBeOk();

        var line = await LineIdsAsync(order, 1);
        Assert.Equal(
            [
                Expect(Clerk, "sales-order.created", order.No, null, Summary("Draft", ids, 1, "5")),                                      // as computed: 1 x 5, no scale
                Expect(Clerk, "sales-order.updated", order.No, Summary("Draft", ids, 1, "5.00"), Summary("Draft", ids, 1, "4.5")),         // before: reloaded, 5.00
                Expect(Clerk, "sales-order.price-changed", order.No, null, PriceChanged(1, line, "Customer", "5.00", "4.5"))
            ],
            await TrailAsync(order));
        await AssertListedNewestFirstAsync(order, "sales-order.price-changed", "sales-order.updated", "sales-order.created");
    }

    /// <summary>The audit list (GET /api/audit) sorts by Id descending, so two rows swapped in the table would show swapped here.</summary>
    private async Task AssertListedNewestFirstAsync(SalesOrderDto order, params string[] actions)
    {
        var page = (await _s.Admin.Audit.QueryAsync(entityType: AuditEntities.SalesOrder, entityId: order.No)).Data<AuditPage>();
        Assert.Equal(actions, page.Items.Select(i => i.Action));
    }

    // ===== submit =====
    [Theory]
    [InlineData("100", "Approved", false, false, "100.00")]      // below every rule: released at once, no approver
    [InlineData("6000", "Submitted", true, false, "6000.00")]    // reaches the 5,000.00 rule: waits for a Supervisor
    [InlineData("25000", "Submitted", true, true, "25000.00")]   // reaches the 20,000.00 rule too: waits for the Owner, large
    public async Task Submit_writes_one_row_with_the_status_change_the_approval_flags_and_the_amount_read_back_from_the_database(string total, string status, bool requires, bool large, string amount)
    {
        var order = await CreateAsync(_s.Clerk, 1m, D(total));

        var submitted = await SubmitAsync(_s.Clerk, order);

        Assert.Equal(status, submitted.Status.ToString());
        Assert.Equal(
            [
                Expect(Clerk, "sales-order.created", order.No, null, Summary("Draft", await HeaderIdsAsync(order), 1, D(total).ToString(CultureInfo.InvariantCulture))),
                Expect(Clerk, "sales-order.submitted", order.No, StatusOnly("Draft"),
                    $$"""{"Status":"{{status}}","RequiresApproval":{{Json(requires)}},"IsLarge":{{Json(large)}},"SellingAmount":{{amount}}}""")
            ],
            await TrailAsync(order));
    }

    private static string Json(bool value) => value ? "true" : "false";

    // ===== approve =====
    [Fact]
    public async Task Approve_by_the_supervisor_writes_one_row_with_the_status_change_and_IsLarge_false_and_the_Owner_of_a_large_order_writes_IsLarge_true()
    {
        var standard = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 6000m));
        var large = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 25000m));

        _ = await ApproveAsync(_s.Supervisor, standard);
        _ = await ApproveAsync(_s.Owner, large);

        var standardRows = await TrailAsync(standard);
        var largeRows = await TrailAsync(large);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.approved"], standardRows.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "sales-order.approved", standard.No, StatusOnly("Submitted"), """{"Status":"Approved","IsLarge":false}"""), standardRows[2]);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.approved"], largeRows.Select(r => r.Action));
        Assert.Equal(Expect(Owner, "sales-order.approved", large.No, StatusOnly("Submitted"), """{"Status":"Approved","IsLarge":true}"""), largeRows[2]);
    }

    // ===== reject =====
    [Fact]
    public async Task Reject_writes_one_row_with_the_status_change_and_the_trimmed_reason_and_the_order_goes_back_to_Draft()
    {
        var submitted = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 6000m));

        var rejected = (await _s.Supervisor.SalesOrders.RejectAsync(submitted.Id, "  Price too high  ", submitted.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Draft, rejected.Status);
        var trail = await TrailAsync(submitted);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.rejected"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "sales-order.rejected", submitted.No, StatusOnly("Submitted"), """{"Status":"Draft","Reason":"Price too high"}"""), trail[2]);
    }

    // ===== recall =====
    [Fact]
    public async Task Recall_writes_one_row_with_the_status_and_the_approval_flag_it_had_and_a_repeat_of_the_recall_writes_nothing()
    {
        var submitted = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 6000m));

        var recalled = (await _s.Clerk.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>();
        _ = (await _s.Clerk.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();     // a double tap with the old token

        Assert.Equal(SalesOrderStatus.Draft, recalled.Status);
        var trail = await TrailAsync(submitted);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.recalled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "sales-order.recalled", submitted.No, """{"Status":"Submitted","RequiresApproval":true}""", StatusOnly("Draft")), trail[2]);
    }

    // ===== cancel =====
    [Fact]
    public async Task Cancel_of_a_Draft_writes_one_row_with_a_null_reason_when_none_is_given()
    {
        var draft = await CreateAsync(_s.Clerk, 1m, 3m);

        _ = (await _s.Clerk.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(draft);
        Assert.Equal(["sales-order.created", "sales-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "sales-order.cancelled", draft.No, StatusOnly("Draft"), """{"Status":"Cancelled","Reason":null}"""), trail[1]);
    }

    [Fact]
    public async Task Cancel_of_a_Submitted_order_by_its_submitter_writes_one_row_with_the_trimmed_reason()
    {
        var submitted = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 6000m));

        _ = (await _s.Clerk.SalesOrders.CancelAsync(submitted.Id, "  Customer changed their mind  ", submitted.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(submitted);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "sales-order.cancelled", submitted.No, StatusOnly("Submitted"), """{"Status":"Cancelled","Reason":"Customer changed their mind"}"""), trail[2]);
    }

    [Fact]
    public async Task Cancel_of_an_Approved_order_by_its_reviewer_writes_one_row_with_the_reason()
    {
        var approved = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, 1m, 100m));

        _ = (await _s.Supervisor.SalesOrders.CancelAsync(approved.Id, "Customer withdrew the order", approved.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(approved);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "sales-order.cancelled", approved.No, StatusOnly("Approved"), """{"Status":"Cancelled","Reason":"Customer withdrew the order"}"""), trail[2]);
    }

    // ===== deliveries, close and auto-close =====
    /// <summary>A product with <paramref name="onHand"/> in the default warehouse and an Approved order (released at once, below every rule) for <paramref name="ordered"/> of it at 5.00.</summary>
    private async Task<(ProductDto Product, SalesOrderDto Order)> ApprovedOrderAsync(decimal ordered, decimal onHand)
    {
        var product = await _s.NewProductAsync();
        await Stock.ReceiveAsync(_s.Warehouse, new Line(product.Id, onHand, 2m));
        var order = await SubmitAsync(_s.Clerk, await CreateAsync(_s.Clerk, ordered, 5m, product: product.Id));
        Assert.Equal(SalesOrderStatus.Approved, order.Status);
        return (product, order);
    }

    /// <summary>A delivery of <paramref name="qty"/> of the order's one line, posted by the Administrator.</summary>
    private async Task<DocumentDto> DeliverAsync(SalesOrderDto order, ProductDto product, decimal qty)
    {
        var draft = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        var saved = (await _s.Clerk.Documents.UpdateAsync(draft.Id, new DocumentSaveRequest(draft.WarehouseId, draft.PartnerId, IssuePurpose.Sale, null, [new DocumentLineInput(product.Id, qty, null, null, null, null, 1)]), draft.ConcurrencyToken)).Data<DocumentDto>();
        var submitted = (await _s.Clerk.Documents.SubmitAsync(saved.Id, saved.ConcurrencyToken)).Data<DocumentDto>();
        if (submitted.RequiresApproval) (await Stock.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        var ready = (await _s.Admin.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
        return (await _s.Admin.Documents.PostAsync(ready.Id, ready.ConcurrencyToken)).Data<DocumentDto>();
    }

    [Fact]
    public async Task Close_short_writes_one_row_with_the_status_the_kind_Short_and_the_trimmed_reason_and_an_earlier_partial_delivery_writes_no_order_row()
    {
        var (product, order) = await ApprovedOrderAsync(10m, onHand: 50m);
        _ = await DeliverAsync(order, product, 4m);
        var read = await _s.ReadAsync(order.Id);

        _ = (await _s.Admin.SalesOrders.CloseAsync(order.Id, "  Customer will take no more  ", read.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(order);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.closed"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Admin, "sales-order.closed", order.No, StatusOnly("Approved"), """{"Status":"Closed","CloseKind":"Short","Reason":"Customer will take no more"}"""), trail[2]);
    }

    [Fact]
    public async Task Posting_the_last_delivery_writes_exactly_one_Closed_row_for_the_order_before_the_Posted_row_of_the_delivery_and_a_partial_one_writes_none()
    {
        var (product, order) = await ApprovedOrderAsync(10m, onHand: 50m);

        var first = await DeliverAsync(order, product, 6m);
        Assert.Equal(["sales-order.created", "sales-order.submitted"], (await TrailAsync(order)).Select(r => r.Action));   // a partial delivery closes nothing and writes nothing on the order
        var last = await DeliverAsync(order, product, 4m);

        Assert.Equal(SalesOrderStatus.Closed, (await _s.ReadAsync(order.Id)).Status);
        var trail = await TrailAsync(order);
        Assert.Equal(["sales-order.created", "sales-order.submitted", "sales-order.closed"], trail.Select(r => r.Action));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderClosed), ("@no", order.No)));
        Assert.Equal(Expect(Admin, "sales-order.closed", order.No, StatusOnly("Approved"), $$"""{"Status":"Closed","CloseKind":"Delivered","Delivery":"{{last.No}}"}"""), trail[2]);

        // The order of the two rows is pinned too: the Closed row of the order is staged by AfterPost, BEFORE the handler stages the Posted row of the delivery, so it has the lower Id (DDD-17 must not swap them
        // without saying so; its card in the roadmap expects the opposite order).
        var posted = Assert.Single(await RowsAsync(AuditEntities.StockDocument, last.No), r => r.Action == AuditActions.DocumentPosted);
        var closed = Assert.Single(await RowsAsync(AuditEntities.SalesOrder, order.No), r => r.Action == AuditActions.SalesOrderClosed);
        Assert.True(closed.Id < posted.Id, $"Closed row {closed.Id}, Posted row {posted.Id}");
        Assert.NotEqual(first.No, last.No);
    }

    // ===== who sees the amounts =====
    private ApiClient Token(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private static readonly string[] SellingAmounts = ["SellingAmount", "ListedPrice", "EnteredPrice", "7000", "6500", "6400"];

    [Fact]
    public async Task The_audit_read_of_a_caller_without_so_manage_or_pricelist_manage_shows_no_selling_amount_in_any_row_of_the_order_and_the_other_keys_are_as_written()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().ForCustomer(customer.Id).WithItem(product.Id, 7000m));
        var order = await CreateAsync(_s.Clerk, 1m, 6500m, customer.Id, product.Id);                                  // created, price-changed
        _ = (await _s.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_s.Warehouse, customer.Id).WithLine(product.Id, 1m, 6400m).Build(), order.ConcurrencyToken)).ShouldBeOk(); // updated, price-changed
        var saved = await _s.ReadAsync(order.Id);
        var submitted = await SubmitAsync(_s.Clerk, saved);                                                          // submitted
        _ = await ApproveAsync(_s.Supervisor, submitted);                                                            // approved
        var ids = await HeaderIdsAsync(order);
        var line = await LineIdsAsync(order, 1);

        async Task<List<AuditEntry>> ReadAsync(params string[] permissions) =>
            (await Token(permissions).Audit.QueryAsync(entityType: AuditEntities.SalesOrder, entityId: order.No)).ShouldBeOk().Data<AuditPage>().Items.Reverse().ToList();   // newest first on the page: oldest first here
        var blind = await ReadAsync(Permissions.AuditRead, Permissions.SoRead, Permissions.CostView, Permissions.ValuationView);
        var sighted = await ReadAsync(Permissions.AuditRead, Permissions.SoManage);
        var rawRows = await RowsAsync(AuditEntities.SalesOrder, order.No);

        // The same six rows, in the same order, for both readers; only the keys tagged with so.manage are missing for the one without it.
        Assert.Equal(["sales-order.created", "sales-order.price-changed", "sales-order.updated", "sales-order.price-changed", "sales-order.submitted", "sales-order.approved"], rawRows.Select(r => r.Action));
        Assert.Equal(rawRows.Select(r => r.Action), blind.Select(e => e.Action));
        Assert.Equal(rawRows.Select(r => r.Action), sighted.Select(e => e.Action));
        foreach (var entry in blind)
            foreach (var secret in SellingAmounts)
                Assert.False((entry.Before ?? "").Contains(secret) || (entry.After ?? "").Contains(secret), $"{entry.Action} shows {secret} to a caller without so.manage or pricelist.manage: {entry.Before} / {entry.After}");

        Assert.Equal(
            [
                (Clerk, "sales-order.created", null, $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1}"""),
                (Clerk, "sales-order.price-changed", null, $$"""{"Line":1,"ProductId":{{line.Product}},"UomId":{{line.Uom}},"Source":"Customer"}"""),
                (Clerk, "sales-order.updated", $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1}""", $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1}"""),
                (Clerk, "sales-order.price-changed", null, $$"""{"Line":1,"ProductId":{{line.Product}},"UomId":{{line.Uom}},"Source":"Customer"}"""),
                (Clerk, "sales-order.submitted", """{"Status":"Draft"}""", """{"Status":"Submitted","RequiresApproval":true,"IsLarge":false}"""),
                (Supervisor, "sales-order.approved", """{"Status":"Submitted"}""", """{"Status":"Approved","IsLarge":false}""")
            ],
            blind.Select(e => (e.Actor, e.Action, e.Before, e.After)));

        // The reader with so.manage sees the rows as they are stored, byte for byte: the same strings as the table holds, selling amounts included, each at the scale it was written in.
        Assert.Equal(rawRows.Select(r => (r.Actor, r.Action, r.Before, r.After)), sighted.Select(e => (e.Actor, e.Action, e.Before, e.After)));
        Assert.Equal(
            [
                $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1,"SellingAmount":6500}""",
                PriceChanged(1, line, "Customer", "7000.00", "6500"),
                PriceChanged(1, line, "Customer", "7000.00", "6400"),
                """{"Status":"Submitted","RequiresApproval":true,"IsLarge":false,"SellingAmount":6400.00}"""
            ],
            sighted.Where(e => e.Action is "sales-order.created" or "sales-order.price-changed" or "sales-order.submitted").Select(e => e.After!));
        var updated = Assert.Single(sighted, e => e.Action == "sales-order.updated");
        Assert.Equal(
            ($$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1,"SellingAmount":6500.00}""",
             $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"CustomerId":{{ids.Customer}},"Lines":1,"SellingAmount":6400}"""),
            (updated.Before, updated.After));
    }
}
