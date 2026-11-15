using System.Globalization;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.Data.SqlClient;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// DDD-12-0, a characterization: the audit rows every purchase-order verb writes TODAY, pinned before the purchase order raises events and <c>PurchaseOrderAuditSubscriber</c> writes the rows. Nothing here states
/// a wish; it states what <c>main</c> does, and the rows must stay byte for byte the same after DDD-12 (the twin of <c>SalesOrderAuditRowsTests</c>, PR #283).
/// <para>
/// What is compared: the whole trail of one order, read straight from the <c>AuditLogs</c> table in the order of its <c>Id</c>, as (actor, action, entity type, entity id, before, after) with the
/// <c>Before</c> and <c>After</c> columns as RAW strings (key order, enum names, nulls and decimal scale included: 12, 12.5 and 12.50 are three different strings). The expected strings are written out in
/// full with the real internal ids put in, so nothing is normalised except two generated values: the row <c>Id</c> (only its ORDER is used) and <c>At</c> (only checked to be a recent UTC time).
/// The order number, the actor names and the internal warehouse and supplier ids are exact.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderAuditRowsTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private static readonly string Clerk = TestUsers.UsernameOf(Roles.Clerk);
    private static readonly string Supervisor = TestUsers.UsernameOf(Roles.Supervisor);
    private static readonly string Owner = TestUsers.UsernameOf(Roles.Owner);
    private static readonly string Admin = TestUsers.UsernameOf(Roles.Admin);

    // ----- reading the rows -----
    private sealed record Row(long Id, string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);

    /// <summary>The (actor, action, entity type, entity id, before, after) of a row: everything pinned except the generated Id and At.</summary>
    private sealed record Pinned(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After);

    private static Pinned Expect(string actor, string action, string no, string? before, string? after) => new(actor, action, AuditEntities.PurchaseOrder, no, before, after);

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
    private async Task<List<Pinned>> TrailAsync(PurchaseOrderDto order)
    {
        var rows = await RowsAsync(AuditEntities.PurchaseOrder, order.No);
        Assert.All(rows, r => Assert.True(Math.Abs((DateTime.UtcNow - r.At).TotalMinutes) < 10, $"At {r.At:o} is not a recent UTC time"));
        return rows.Select(r => new Pinned(r.Actor, r.Action, r.EntityType, r.EntityId, r.Before, r.After)).ToList();
    }

    // ----- the ids the payloads carry (internal, generated: read back, not normalised) -----
    private async Task<(int Warehouse, int Supplier)> HeaderIdsAsync(PurchaseOrderDto order)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT WarehouseId, SupplierId FROM PurchaseOrder WHERE PublicId = @id";
        command.Parameters.AddWithValue("@id", order.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    // ----- the payload shapes, written out in full (raw JSON, key order as written by the handlers) -----
    private static string Summary(string status, (int Warehouse, int Supplier) ids, int lines, string amount) =>
        $$"""{"Status":"{{status}}","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":{{lines}},"Amount":{{amount}}}""";

    private static string StatusOnly(string status) => $$"""{"Status":"{{status}}"}""";

    private static string Json(bool value) => value ? "true" : "false";

    private static decimal D(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture); // keeps the scale the text has: "12.50" is 12.50, not 12.5

    // ----- making orders -----
    private async Task<PurchaseOrderDto> CreateAsync(ApiClient by, decimal qty, decimal price, Guid? product = null, Guid? supplier = null)
    {
        var id = product ?? (await _p.NewProductAsync()).Id;
        return (await by.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, supplier ?? fixture.Reference.SupplierId).WithLine(id, qty, price).Build())).Data<PurchaseOrderDto>();
    }

    private static async Task<PurchaseOrderDto> SubmitAsync(ApiClient by, PurchaseOrderDto order) => (await by.PurchaseOrders.SubmitAsync(order.Id, order.ConcurrencyToken)).Data<PurchaseOrderDto>();

    private static async Task<PurchaseOrderDto> ApproveAsync(ApiClient by, PurchaseOrderDto order) => (await by.PurchaseOrders.ApproveAsync(order.Id, order.ConcurrencyToken)).Data<PurchaseOrderDto>();

    // ===== create =====
    [Theory]
    [InlineData("1", "12", "12")]
    [InlineData("1", "12.5", "12.5")]
    [InlineData("1", "12.50", "12.50")]
    [InlineData("10", "2.50", "25.00")]
    [InlineData("1.5", "12.5", "18.75")]
    public async Task Create_writes_one_row_with_the_status_the_header_ids_the_line_count_and_the_amount_at_the_scale_it_was_computed_in_memory(string qty, string price, string amountAsWritten)
    {
        var order = await CreateAsync(_p.Clerk, D(qty), D(price));

        var ids = await HeaderIdsAsync(order);
        Assert.Equal([Expect(Clerk, "purchase-order.created", order.No, null, Summary("Draft", ids, 1, amountAsWritten))], await TrailAsync(order));
    }

    // ===== update =====
    [Fact]
    public async Task Update_writes_one_row_whose_before_is_read_back_from_the_database_at_two_decimals_and_whose_after_is_the_new_amount_as_computed_in_memory()
    {
        var product = await _p.NewProductAsync();
        var order = await CreateAsync(_p.Clerk, 10m, 2.50m, product.Id);
        var ids = await HeaderIdsAsync(order);
        var supplier = fixture.Reference.SupplierId;

        var first = (await _p.Clerk.PurchaseOrders.UpdateAsync(order.Id, new PurchaseOrderBuilder(_p.Warehouse, supplier).WithLine(product.Id, 1m, 12m).Build(), order.ConcurrencyToken)).Data<PurchaseOrderDto>();
        var second = (await _p.Clerk.PurchaseOrders.UpdateAsync(order.Id, new PurchaseOrderBuilder(_p.Warehouse, supplier).WithLine(product.Id, 1m, 12.5m).WithLine(product.Id, 2m, 1.50m).Build(), first.ConcurrencyToken)).Data<PurchaseOrderDto>();
        _ = (await _p.Clerk.PurchaseOrders.UpdateAsync(order.Id, new PurchaseOrderBuilder(_p.Warehouse, supplier).WithRemark("r").WithLine(product.Id, 3m, 4m).Build(), second.ConcurrencyToken)).ShouldBeOk();

        Assert.Equal(
            [
                Expect(Clerk, "purchase-order.created", order.No, null, Summary("Draft", ids, 1, "25.00")),
                Expect(Clerk, "purchase-order.updated", order.No, Summary("Draft", ids, 1, "25.00"), Summary("Draft", ids, 1, "12")),        // before: the stored 25.00; after: 1 x 12 as computed
                Expect(Clerk, "purchase-order.updated", order.No, Summary("Draft", ids, 1, "12.00"), Summary("Draft", ids, 2, "15.50")),    // before: the stored amount reloaded, "12" has become "12.00"
                Expect(Clerk, "purchase-order.updated", order.No, Summary("Draft", ids, 2, "15.50"), Summary("Draft", ids, 1, "12"))         // 3 x 4 = 12, again without a scale
            ],
            await TrailAsync(order));
    }

    [Fact]
    public async Task Update_that_changes_the_supplier_writes_one_row_whose_before_still_names_the_old_supplier()
    {
        var product = await _p.NewProductAsync();
        var order = await CreateAsync(_p.Clerk, 2m, 5m, product.Id);
        var before = await HeaderIdsAsync(order);
        var other = await _p.NewPartnerAsync();

        _ = (await _p.Clerk.PurchaseOrders.UpdateAsync(order.Id, new PurchaseOrderBuilder(_p.Warehouse, other.Id).WithLine(product.Id, 2m, 5m).Build(), order.ConcurrencyToken)).ShouldBeOk();

        var after = await HeaderIdsAsync(order);
        Assert.NotEqual(before.Supplier, after.Supplier);
        Assert.Equal(
            [
                Expect(Clerk, "purchase-order.created", order.No, null, Summary("Draft", before, 1, "10")),
                Expect(Clerk, "purchase-order.updated", order.No, Summary("Draft", before, 1, "10.00"), Summary("Draft", after, 1, "10"))
            ],
            await TrailAsync(order));
    }

    // ===== submit =====
    [Theory]
    [InlineData("100", "Approved", false, false, "100.00")]      // below every rule: released at once, no approver
    [InlineData("6000", "Submitted", true, false, "6000.00")]    // reaches the 5,000.00 rule: waits for a Supervisor
    [InlineData("25000", "Submitted", true, true, "25000.00")]   // reaches the 20,000.00 rule too: waits for the Owner, large
    public async Task Submit_writes_one_row_with_the_status_change_the_approval_flags_and_the_amount_read_back_from_the_database(string total, string status, bool requires, bool large, string amount)
    {
        var order = await CreateAsync(_p.Clerk, 1m, D(total));

        var submitted = await SubmitAsync(_p.Clerk, order);

        Assert.Equal(status, submitted.Status.ToString());
        Assert.Equal(
            [
                Expect(Clerk, "purchase-order.created", order.No, null, Summary("Draft", await HeaderIdsAsync(order), 1, D(total).ToString(CultureInfo.InvariantCulture))),
                Expect(Clerk, "purchase-order.submitted", order.No, StatusOnly("Draft"),
                    $$"""{"Status":"{{status}}","RequiresApproval":{{Json(requires)}},"IsLarge":{{Json(large)}},"Amount":{{amount}}}""")
            ],
            await TrailAsync(order));
    }

    // ===== approve =====
    [Fact]
    public async Task Approve_by_the_supervisor_writes_one_row_with_the_status_change_and_IsLarge_false_and_the_Owner_of_a_large_order_writes_IsLarge_true()
    {
        var standard = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 6000m));
        var large = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 25000m));

        _ = await ApproveAsync(_p.Supervisor, standard);
        _ = await ApproveAsync(_p.Owner, large);

        var standardRows = await TrailAsync(standard);
        var largeRows = await TrailAsync(large);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.approved"], standardRows.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "purchase-order.approved", standard.No, StatusOnly("Submitted"), """{"Status":"Approved","IsLarge":false}"""), standardRows[2]);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.approved"], largeRows.Select(r => r.Action));
        Assert.Equal(Expect(Owner, "purchase-order.approved", large.No, StatusOnly("Submitted"), """{"Status":"Approved","IsLarge":true}"""), largeRows[2]);
    }

    // ===== reject =====
    [Fact]
    public async Task Reject_writes_one_row_with_the_status_change_and_the_trimmed_reason_and_the_order_goes_back_to_Draft()
    {
        var submitted = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 6000m));

        var rejected = (await _p.Supervisor.PurchaseOrders.RejectAsync(submitted.Id, "  Price too high  ", submitted.ConcurrencyToken)).Data<PurchaseOrderDto>();

        Assert.Equal(PurchaseOrderStatus.Draft, rejected.Status);
        var trail = await TrailAsync(submitted);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.rejected"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "purchase-order.rejected", submitted.No, StatusOnly("Submitted"), """{"Status":"Draft","Reason":"Price too high"}"""), trail[2]);
    }

    // ===== recall =====
    [Fact]
    public async Task Recall_writes_one_row_with_the_status_and_the_approval_flag_it_had_and_a_repeat_of_the_recall_writes_nothing()
    {
        var submitted = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 6000m));

        var recalled = (await _p.Clerk.PurchaseOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<PurchaseOrderDto>();
        _ = (await _p.Clerk.PurchaseOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();     // a double tap with the old token

        Assert.Equal(PurchaseOrderStatus.Draft, recalled.Status);
        var trail = await TrailAsync(submitted);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.recalled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "purchase-order.recalled", submitted.No, """{"Status":"Submitted","RequiresApproval":true}""", StatusOnly("Draft")), trail[2]);
    }

    // ===== cancel =====
    [Fact]
    public async Task Cancel_of_a_Draft_writes_one_row_with_a_null_reason_when_none_is_given()
    {
        var draft = await CreateAsync(_p.Clerk, 1m, 3m);

        _ = (await _p.Clerk.PurchaseOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(draft);
        Assert.Equal(["purchase-order.created", "purchase-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "purchase-order.cancelled", draft.No, StatusOnly("Draft"), """{"Status":"Cancelled","Reason":null}"""), trail[1]);
    }

    [Fact]
    public async Task Cancel_of_a_Draft_with_a_reason_of_blanks_writes_a_null_reason()
    {
        var draft = await CreateAsync(_p.Clerk, 1m, 3m);

        _ = (await _p.Clerk.PurchaseOrders.CancelAsync(draft.Id, "    ", draft.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(draft);
        Assert.Equal(Expect(Clerk, "purchase-order.cancelled", draft.No, StatusOnly("Draft"), """{"Status":"Cancelled","Reason":null}"""), trail[1]);
    }

    [Fact]
    public async Task Cancel_of_a_Draft_of_another_user_writes_one_row_by_the_canceller_with_the_trimmed_reason()
    {
        var draft = await CreateAsync(_p.Clerk, 1m, 3m);

        _ = (await _p.Admin.PurchaseOrders.CancelAsync(draft.Id, "  The clerk has left  ", draft.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(draft);
        Assert.Equal(["purchase-order.created", "purchase-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Admin, "purchase-order.cancelled", draft.No, StatusOnly("Draft"), """{"Status":"Cancelled","Reason":"The clerk has left"}"""), trail[1]);
    }

    [Fact]
    public async Task Cancel_of_a_Submitted_order_by_its_submitter_writes_one_row_with_the_trimmed_reason()
    {
        var submitted = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 6000m));

        _ = (await _p.Clerk.PurchaseOrders.CancelAsync(submitted.Id, "  Supplier changed the price  ", submitted.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(submitted);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "purchase-order.cancelled", submitted.No, StatusOnly("Submitted"), """{"Status":"Cancelled","Reason":"Supplier changed the price"}"""), trail[2]);
    }

    [Fact]
    public async Task Cancel_of_an_Approved_order_by_its_reviewer_writes_one_row_with_the_reason()
    {
        var approved = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 100m));
        Assert.Equal(PurchaseOrderStatus.Approved, approved.Status);

        _ = (await _p.Supervisor.PurchaseOrders.CancelAsync(approved.Id, "Supplier withdrew the offer", approved.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(approved);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.cancelled"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Supervisor, "purchase-order.cancelled", approved.No, StatusOnly("Approved"), """{"Status":"Cancelled","Reason":"Supplier withdrew the offer"}"""), trail[2]);
    }

    // ===== receipts, close and auto-close =====
    /// <summary>An Approved order (released at once, below every rule) of <paramref name="ordered"/> of a new product at 5.00.</summary>
    private async Task<(ProductDto Product, PurchaseOrderDto Order)> ApprovedOrderAsync(decimal ordered)
    {
        var product = await _p.NewProductAsync();
        var order = await _p.ApprovedAsync((product.Id, ordered, 5m, null));
        return (product, order);
    }

    /// <summary>A receipt of <paramref name="qty"/> of the order's one line, posted (the Administrator posts in these tests).</summary>
    private async Task<DocumentDto> ReceiveAsync(PurchaseOrderDto order, ProductDto product, decimal qty)
    {
        var receipt = await _p.ReceiptFromAsync(order);
        var saved = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(product.Id, qty, 1)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        return await _p.PostAsync(saved, _p.Clerk);
    }

    [Fact]
    public async Task Close_short_writes_one_row_with_the_status_the_kind_Short_and_the_trimmed_reason_and_an_earlier_partial_receipt_writes_no_order_row()
    {
        var (product, order) = await ApprovedOrderAsync(10m);
        _ = await ReceiveAsync(order, product, 4m);
        var read = await _p.ReadAsync(order.Id);

        _ = (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "  Supplier ships no more  ", read.ConcurrencyToken)).ShouldBeOk();

        var trail = await TrailAsync(order);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.closed"], trail.Select(r => r.Action));
        Assert.Equal(Expect(Clerk, "purchase-order.closed", order.No, StatusOnly("Approved"), """{"Status":"Closed","CloseKind":"Short","Reason":"Supplier ships no more"}"""), trail[2]);
    }

    [Fact]
    public async Task Posting_the_last_receipt_writes_exactly_one_Closed_row_for_the_order_before_the_Posted_row_of_the_receipt_and_a_partial_one_writes_none()
    {
        var (product, order) = await ApprovedOrderAsync(10m);

        var first = await ReceiveAsync(order, product, 6m);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted"], (await TrailAsync(order)).Select(r => r.Action));   // a partial receipt closes nothing and writes nothing on the order
        var last = await ReceiveAsync(order, product, 4m);

        Assert.Equal(PurchaseOrderStatus.Closed, (await _p.ReadAsync(order.Id)).Status);
        var trail = await TrailAsync(order);
        Assert.Equal(["purchase-order.created", "purchase-order.submitted", "purchase-order.closed"], trail.Select(r => r.Action));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.PurchaseOrderClosed), ("@no", order.No)));
        Assert.Equal(Expect(Admin, "purchase-order.closed", order.No, StatusOnly("Approved"), $$"""{"Status":"Closed","CloseKind":"Received","Receipt":"{{last.No}}"}"""), trail[2]);

        // The order of the two rows is pinned too: the Closed row of the order is staged by AfterPost, BEFORE the handler stages the Posted row of the receipt, so it has the lower Id (DDD-17 must not swap them
        // without saying so).
        var posted = Assert.Single(await RowsAsync(AuditEntities.StockDocument, last.No), r => r.Action == AuditActions.DocumentPosted);
        var closed = Assert.Single(await RowsAsync(AuditEntities.PurchaseOrder, order.No), r => r.Action == AuditActions.PurchaseOrderClosed);
        Assert.True(closed.Id < posted.Id, $"Closed row {closed.Id}, Posted row {posted.Id}");
        Assert.NotEqual(first.No, last.No);
    }

    // ===== who sees the amounts =====
    private ApiClient Token(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private static readonly string[] Amounts = ["Amount", "6500", "6400"];

    [Fact]
    public async Task The_audit_read_of_a_caller_without_cost_view_shows_no_amount_in_any_row_of_the_order_and_the_other_keys_are_as_written()
    {
        var order = await CreateAsync(_p.Clerk, 1m, 6500m);                                                          // created
        _ = (await _p.Clerk.PurchaseOrders.UpdateAsync(order.Id, new PurchaseOrderBuilder(_p.Warehouse, fixture.Reference.SupplierId).WithLine((await _p.NewProductAsync()).Id, 1m, 6400m).Build(), order.ConcurrencyToken)).ShouldBeOk(); // updated
        var saved = await _p.ReadAsync(order.Id);
        var submitted = await SubmitAsync(_p.Clerk, saved);                                                          // submitted
        _ = await ApproveAsync(_p.Supervisor, submitted);                                                            // approved
        var ids = await HeaderIdsAsync(order);

        async Task<List<AuditEntry>> ReadAsync(params string[] permissions) =>
            (await Token(permissions).Audit.QueryAsync(entityType: AuditEntities.PurchaseOrder, entityId: order.No)).ShouldBeOk().Data<AuditPage>().Items.Reverse().ToList();   // newest first on the page: oldest first here
        var blind = await ReadAsync(Permissions.AuditRead, Permissions.PoRead, Permissions.ValuationView);
        var sighted = await ReadAsync(Permissions.AuditRead, Permissions.PoManage, Permissions.CostView);
        var rawRows = await RowsAsync(AuditEntities.PurchaseOrder, order.No);

        // The same four rows, in the same order, for both readers; only the keys tagged with cost.view are missing for the one without it.
        Assert.Equal(["purchase-order.created", "purchase-order.updated", "purchase-order.submitted", "purchase-order.approved"], rawRows.Select(r => r.Action));
        Assert.Equal(rawRows.Select(r => r.Action), blind.Select(e => e.Action));
        Assert.Equal(rawRows.Select(r => r.Action), sighted.Select(e => e.Action));
        foreach (var entry in blind)
            foreach (var secret in Amounts)
                Assert.False((entry.Before ?? "").Contains(secret) || (entry.After ?? "").Contains(secret), $"{entry.Action} shows {secret} to a caller without cost.view: {entry.Before} / {entry.After}");

        Assert.Equal(
            [
                (Clerk, "purchase-order.created", null, $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1}"""),
                (Clerk, "purchase-order.updated", $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1}""", $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1}"""),
                (Clerk, "purchase-order.submitted", """{"Status":"Draft"}""", """{"Status":"Submitted","RequiresApproval":true,"IsLarge":false}"""),
                (Supervisor, "purchase-order.approved", """{"Status":"Submitted"}""", """{"Status":"Approved","IsLarge":false}""")
            ],
            blind.Select(e => (e.Actor, e.Action, e.Before, e.After)));

        // The reader with cost.view sees the rows as they are stored, byte for byte: the same strings as the table holds, amounts included, each at the scale it was written in.
        Assert.Equal(rawRows.Select(r => (r.Actor, r.Action, r.Before, r.After)), sighted.Select(e => (e.Actor, e.Action, e.Before, e.After)));
        Assert.Equal(
            [
                $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1,"Amount":6500}""",
                """{"Status":"Submitted","RequiresApproval":true,"IsLarge":false,"Amount":6400.00}"""
            ],
            sighted.Where(e => e.Action is "purchase-order.created" or "purchase-order.submitted").Select(e => e.After!));
        var updated = Assert.Single(sighted, e => e.Action == "purchase-order.updated");
        Assert.Equal(
            ($$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1,"Amount":6500.00}""",
             $$"""{"Status":"Draft","WarehouseId":{{ids.Warehouse}},"SupplierId":{{ids.Supplier}},"Lines":1,"Amount":6400}"""),
            (updated.Before, updated.After));
    }

    /// <summary>The audit list (GET /api/audit) sorts by Id descending, so two rows swapped in the table would show swapped here.</summary>
    [Fact]
    public async Task The_audit_list_shows_the_rows_of_an_order_newest_first()
    {
        var submitted = await SubmitAsync(_p.Clerk, await CreateAsync(_p.Clerk, 1m, 6000m));
        _ = await ApproveAsync(_p.Supervisor, submitted);

        var page = (await _p.Admin.Audit.QueryAsync(entityType: AuditEntities.PurchaseOrder, entityId: submitted.No)).Data<AuditPage>();

        Assert.Equal(["purchase-order.approved", "purchase-order.submitted", "purchase-order.created"], page.Items.Select(i => i.Action));
    }
}
