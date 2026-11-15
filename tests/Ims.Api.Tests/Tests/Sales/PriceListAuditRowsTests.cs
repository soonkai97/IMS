using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// DDD-13, a characterization: the audit rows every price-list verb writes TODAY (create, update, set customer, delete), pinned on <c>main</c> before the aggregate raises events and a subscriber writes the rows
/// instead of the handlers. Nothing here states a wish; it states what <c>main</c> does, and the rows must stay byte for byte the same afterwards. Same method as <c>SalesOrderAuditRowsTests</c> (DDD-11-0).
/// <para>
/// What is compared: the whole trail of ONE list, read straight from the <c>AuditLogs</c> table in the order of its <c>Id</c>, as (actor, action, entity type, entity id, before, after) with the <c>Before</c> and
/// <c>After</c> columns as RAW strings (key order, nulls and the words of the price changes included). The expected strings are written out in full with the real internal ids put in; nothing is normalised
/// except two generated values: the row <c>Id</c> (only its ORDER is used) and <c>At</c> (only checked to be a recent UTC time). A price is only ever written as words with two places ("3.00", "12.50"), so
/// a price entered as 3, 3.5 or 3.50 gives the same words; the scale of the number does not reach the row.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListAuditRowsTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private static readonly string Admin = TestUsers.UsernameOf(Roles.Admin);
    private static readonly string Supervisor = TestUsers.UsernameOf(Roles.Supervisor);

    // ----- reading the rows -----
    private sealed record Row(long Id, string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);

    /// <summary>The (actor, action, entity type, entity id, before, after) of a row: everything pinned except the generated Id and At.</summary>
    private sealed record Pinned(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After);

    private static Pinned Expect(string actor, string action, Guid list, string? before, string? after) => new(actor, action, AuditEntities.PriceList, list.ToString(), before, after);

    private async Task<List<Row>> RowsAsync(Guid list)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Actor, Action, EntityType, EntityId, [Before], [After], [At] FROM AuditLogs WHERE EntityType = @t AND EntityId = @id ORDER BY Id";
        command.Parameters.AddWithValue("@t", AuditEntities.PriceList);
        command.Parameters.AddWithValue("@id", list.ToString());
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Row(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7)));
        return rows;
    }

    /// <summary>The trail of one list: its rows, oldest first (by Id), each written moments ago.</summary>
    private async Task<List<Pinned>> TrailAsync(Guid list)
    {
        var rows = await RowsAsync(list);
        Assert.All(rows, r => Assert.True(Math.Abs((DateTime.UtcNow - r.At).TotalMinutes) < 10, $"At {r.At:o} is not a recent UTC time"));
        return rows.Select(r => new Pinned(r.Actor, r.Action, r.EntityType, r.EntityId, r.Before, r.After)).ToList();
    }

    private Task<int> TotalRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityType = @t", ("@t", AuditEntities.PriceList));

    /// <summary>The internal id of a partner, as the payload carries it.</summary>
    private Task<int> CustomerIdAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM Partner WHERE PublicId = @id", ("@id", publicId));

    // ----- the payload shapes, written out in full (raw JSON, key order as written today) -----
    private static string Header(string name, bool isDefault, int? customer, int items, params string[] words) =>
        $$"""{"Name":"{{name}}","IsDefault":{{(isDefault ? "true" : "false")}},"CustomerId":{{customer?.ToString() ?? "null"}},"Items":{{items}}""" +
        (words.Length == 0 ? "" : ",\"ListPriceChanges\":[" + string.Join(",", words.Select(w => $"\"{w}\"")) + "]") + "}";

    private static string CustomerOnly(int? customer) => $$"""{"CustomerId":{{customer?.ToString() ?? "null"}}}""";

    private static string Added(string sku, string unit, string price) => $"{sku} ({unit}): added at {price}";

    private static string Removed(string sku, string unit, string price) => $"{sku} ({unit}): removed (was {price})";

    private static string Changed(string sku, string unit, string from, string to) => $"{sku} ({unit}): changed from {from} to {to}";

    private Task<ProductDto> ProductAsync(string sku) => _s.NewProductAsync(b => b.WithSku(sku));

    private static string Sku(string prefix) => Unique.Sku(prefix);

    // ===== create =====
    [Fact]
    public async Task Create_writes_one_row_with_no_before_the_header_the_item_count_and_every_price_in_words_in_the_order_given()
    {
        var (a, b, c) = (Sku("A"), Sku("B"), Sku("C"));
        var pa = await ProductAsync(a);
        var pb = await ProductAsync(b);
        var pc = await _s.NewProductAsync(x => x.WithSku(c).WithConversion(fixture.Reference.CartonUnitId, 12m));
        var name = Unique.Name("Retail");

        var list = await _s.NewListAsync(_s.List(name).Default().WithItem(pa.Id, 3m).WithItem(pb.Id, 12.5m).WithItem(pc.Id, 30.50m, fixture.Reference.CartonUnitId));

        Assert.Equal(
            [Expect(Admin, "price-list.created", list.Id, null, Header(name, true, null, 3, Added(a, "PCS", "3.00"), Added(b, "PCS", "12.50"), Added(c, "CTN", "30.50")))],
            await TrailAsync(list.Id));
    }

    [Theory]
    [InlineData("3", "3.00")]
    [InlineData("3.5", "3.50")]
    [InlineData("3.50", "3.50")]
    [InlineData("0.01", "0.01")]
    [InlineData("12345.67", "12345.67")]
    public async Task Create_writes_every_price_with_two_places_whatever_scale_it_was_sent_in(string sent, string written)
    {
        var sku = Sku("S");
        var product = await ProductAsync(sku);
        var name = Unique.Name("Scale");

        var list = await _s.NewListAsync(_s.List(name).WithItem(product.Id, decimal.Parse(sent, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal([Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 1, Added(sku, "PCS", written)))], await TrailAsync(list.Id));
    }

    [Fact]
    public async Task Create_of_a_list_with_no_items_writes_a_row_with_no_price_words_at_all()
    {
        var name = Unique.Name("Empty");

        var list = await _s.NewListAsync(_s.List(name));

        Assert.Equal([Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 0))], await TrailAsync(list.Id));
    }

    [Fact]
    public async Task Create_of_a_customer_list_writes_the_internal_id_of_the_customer_and_the_actor_is_the_one_who_saved()
    {
        var customer = await _s.NewPartnerAsync();
        var sku = Sku("C");
        var product = await ProductAsync(sku);
        var name = Unique.Name("Theirs");

        var list = await _s.NewListAsync(_s.List(name).ForCustomer(customer.Id).WithItem(product.Id, 7m), _s.Supervisor);

        var id = await CustomerIdAsync(customer.Id);
        Assert.Equal([Expect(Supervisor, "price-list.created", list.Id, null, Header(name, false, id, 1, Added(sku, "PCS", "7.00")))], await TrailAsync(list.Id));
    }

    // ===== update =====
    [Fact]
    public async Task Update_writes_one_row_with_the_header_before_and_after_and_the_prices_added_then_removed_then_changed_in_words()
    {
        var (kept, repriced, gone, gone2, new1, new2) = (Sku("K"), Sku("R"), Sku("G"), Sku("H"), Sku("N"), Sku("M"));
        var (pk, pr, pg, pg2, pn1, pn2) = (await ProductAsync(kept), await ProductAsync(repriced), await ProductAsync(gone), await ProductAsync(gone2), await ProductAsync(new1), await ProductAsync(new2));
        var oldName = Unique.Name("Before");
        var newName = Unique.Name("After");
        var list = await _s.NewListAsync(_s.List(oldName).WithItem(pk.Id, 1m).WithItem(pr.Id, 3m).WithItem(pg.Id, 4m).WithItem(pg2.Id, 5.5m));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(newName).Default().WithItem(pn1.Id, 9m).WithItem(pk.Id, 1m).WithItem(pr.Id, 3.5m).WithItem(pn2.Id, 10.5m).Build())).ShouldBeOk();

        // Added in the order the request gave them, then removed in the order the list had them, then changed; an item whose price did not change is not named.
        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(oldName, false, null, 4, Added(kept, "PCS", "1.00"), Added(repriced, "PCS", "3.00"), Added(gone, "PCS", "4.00"), Added(gone2, "PCS", "5.50"))),
                Expect(Admin, "price-list.updated", list.Id, Header(oldName, false, null, 4),
                    Header(newName, true, null, 4, Added(new1, "PCS", "9.00"), Added(new2, "PCS", "10.50"), Removed(gone, "PCS", "4.00"), Removed(gone2, "PCS", "5.50"), Changed(repriced, "PCS", "3.00", "3.50")))
            ],
            await TrailAsync(list.Id));
    }

    [Fact]
    public async Task Update_names_a_price_that_changed_from_3_00_to_3_50_and_a_price_per_carton_by_its_unit()
    {
        var (piece, boxed) = (Sku("P"), Sku("B"));
        var pp = await ProductAsync(piece);
        var pb = await _s.NewProductAsync(x => x.WithSku(boxed).WithConversion(fixture.Reference.CartonUnitId, 12m));
        var name = Unique.Name("Words");
        var list = await _s.NewListAsync(_s.List(name).WithItem(pp.Id, 3m).WithItem(pb.Id, 30m, fixture.Reference.CartonUnitId).WithItem(pb.Id, 2.5m));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(pp.Id, 3.50m).WithItem(pb.Id, 31m, fixture.Reference.CartonUnitId).WithItem(pb.Id, 2.5m).Build())).ShouldBeOk();

        var rows = await TrailAsync(list.Id);
        var updated = Assert.Single(rows, r => r.Action == "price-list.updated");
        Assert.Equal(Header(name, false, null, 3, Changed(piece, "PCS", "3.00", "3.50"), Changed(boxed, "CTN", "30.00", "31.00")), updated.After);
        Assert.Contains($"{piece} (PCS): changed from 3.00 to 3.50", updated.After);
    }

    [Fact]
    public async Task Update_that_changes_nothing_still_writes_a_row_whose_after_is_the_before_and_has_no_price_words()
    {
        var sku = Sku("Q");
        var product = await ProductAsync(sku);
        var name = Unique.Name("Same");
        var list = await _s.NewListAsync(_s.List(name).WithItem(product.Id, 8m));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(product.Id, 8m).Build())).ShouldBeOk();

        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 1, Added(sku, "PCS", "8.00"))),
                Expect(Admin, "price-list.updated", list.Id, Header(name, false, null, 1), Header(name, false, null, 1))
            ],
            await TrailAsync(list.Id));
    }

    [Fact]
    public async Task Update_that_gives_the_list_to_a_customer_and_then_to_another_writes_the_internal_ids_before_and_after()
    {
        var (c1, c2) = (await _s.NewPartnerAsync(), await _s.NewPartnerAsync());
        var name = Unique.Name("Moves");
        var list = await _s.NewListAsync(_s.List(name));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).ForCustomer(c1.Id).Build())).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).ForCustomer(c2.Id).Build())).ShouldBeOk();
        _ = (await _s.Supervisor.PriceLists.UpdateAsync(list.Id, _s.List(name).Build())).ShouldBeOk();

        var (i1, i2) = (await CustomerIdAsync(c1.Id), await CustomerIdAsync(c2.Id));
        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 0)),
                Expect(Admin, "price-list.updated", list.Id, Header(name, false, null, 0), Header(name, false, i1, 0)),
                Expect(Admin, "price-list.updated", list.Id, Header(name, false, i1, 0), Header(name, false, i2, 0)),
                Expect(Supervisor, "price-list.updated", list.Id, Header(name, false, i2, 0), Header(name, false, null, 0))
            ],
            await TrailAsync(list.Id));
    }

    // ===== set customer =====
    [Fact]
    public async Task SetCustomer_writes_one_row_for_every_call_even_when_nothing_changes_with_the_internal_customer_id_before_and_after()
    {
        var (c1, c2) = (await _s.NewPartnerAsync(), await _s.NewPartnerAsync());
        var name = Unique.Name("Owner");
        var list = await _s.NewListAsync(_s.List(name));

        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, c1.Id)).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, c1.Id)).ShouldBeOk();       // the same customer again: still a row
        _ = (await _s.Supervisor.PriceLists.SetCustomerAsync(list.Id, c2.Id)).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, null)).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, null)).ShouldBeOk();         // none to none: still a row

        var (i1, i2) = (await CustomerIdAsync(c1.Id), await CustomerIdAsync(c2.Id));
        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 0)),
                Expect(Admin, "price-list.customer-set", list.Id, CustomerOnly(null), CustomerOnly(i1)),
                Expect(Admin, "price-list.customer-set", list.Id, CustomerOnly(i1), CustomerOnly(i1)),
                Expect(Supervisor, "price-list.customer-set", list.Id, CustomerOnly(i1), CustomerOnly(i2)),
                Expect(Admin, "price-list.customer-set", list.Id, CustomerOnly(i2), CustomerOnly(null)),
                Expect(Admin, "price-list.customer-set", list.Id, CustomerOnly(null), CustomerOnly(null))
            ],
            await TrailAsync(list.Id));
    }

    // ===== delete =====
    [Fact]
    public async Task Delete_writes_the_summary_with_every_price_removed_in_words_as_the_before_and_no_after()
    {
        var customer = await _s.NewPartnerAsync();
        var (a, b) = (Sku("D"), Sku("E"));
        var (pa, pb) = (await ProductAsync(a), await _s.NewProductAsync(x => x.WithSku(b).WithConversion(fixture.Reference.CartonUnitId, 6m)));
        var name = Unique.Name("Doomed");
        var list = await _s.NewListAsync(_s.List(name).ForCustomer(customer.Id).WithItem(pa.Id, 4.25m).WithItem(pb.Id, 22m, fixture.Reference.CartonUnitId));

        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(list.Id)).Status);

        var id = await CustomerIdAsync(customer.Id);
        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(name, false, id, 2, Added(a, "PCS", "4.25"), Added(b, "CTN", "22.00"))),
                Expect(Admin, "price-list.deleted", list.Id, Header(name, false, id, 2, Removed(a, "PCS", "4.25"), Removed(b, "CTN", "22.00")), null)
            ],
            await TrailAsync(list.Id));
    }

    [Fact]
    public async Task Delete_of_a_list_with_no_items_writes_a_before_with_no_price_words_and_no_after()
    {
        var name = Unique.Name("Nothing");
        var list = await _s.NewListAsync(_s.List(name).Default());

        Assert.Equal(HttpStatusCode.NoContent, (await _s.Supervisor.PriceLists.DeleteAsync(list.Id)).Status);

        Assert.Equal(
            [
                Expect(Admin, "price-list.created", list.Id, null, Header(name, true, null, 0)),
                Expect(Supervisor, "price-list.deleted", list.Id, Header(name, true, null, 0), null)
            ],
            await TrailAsync(list.Id));
    }

    // ===== the trail of one list, in the order the rows were written =====
    [Fact]
    public async Task One_request_writes_one_row_and_the_rows_of_one_list_are_in_the_order_of_the_requests()
    {
        var customer = await _s.NewPartnerAsync();
        var sku = Sku("T");
        var product = await ProductAsync(sku);
        var name = Unique.Name("Trail");
        var list = await _s.NewListAsync(_s.List(name).WithItem(product.Id, 1m));
        Assert.Single(await RowsAsync(list.Id));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(product.Id, 2m).Build())).ShouldBeOk();
        Assert.Equal(2, (await RowsAsync(list.Id)).Count);
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id)).ShouldBeOk();
        Assert.Equal(3, (await RowsAsync(list.Id)).Count);
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).ForCustomer(customer.Id).WithItem(product.Id, 2m).Build())).ShouldBeOk();
        Assert.Equal(4, (await RowsAsync(list.Id)).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(list.Id)).Status);

        var rows = await RowsAsync(list.Id);
        Assert.Equal(["price-list.created", "price-list.updated", "price-list.customer-set", "price-list.updated", "price-list.deleted"], rows.Select(r => r.Action));
        Assert.Equal(rows.OrderBy(r => r.Id).Select(r => r.Id), rows.Select(r => r.Id));
    }

    // ===== a refused save writes nothing =====
    [Fact]
    public async Task A_save_the_rules_refuse_or_a_stale_token_writes_no_row_and_leaves_the_rows_already_written_alone()
    {
        var customer = await _s.NewPartnerAsync();
        var sku = Sku("F");
        var product = await ProductAsync(sku);
        var name = Unique.Name("Firm");
        var list = await _s.NewListAsync(_s.List(name).WithItem(product.Id, 1m));
        var other = await _s.NewListAsync(_s.List(Unique.Name("Other")));
        var before = await TotalRowsAsync();

        // a name another list has, a price of zero, a supplier as the customer, a stale token on each verb, a list that does not exist
        Assert.False((await _s.Admin.PriceLists.CreateAsync(_s.List(name).Build())).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.CreateAsync(_s.List().WithItem(product.Id, 0m).Build())).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(product.Id, 0m).Build())).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(other.Name).Build())).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).Build(), "stale")).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.SetCustomerAsync(list.Id, fixture.Reference.SupplierId)).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id, "stale")).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.DeleteAsync(list.Id, "stale")).IsSuccess);
        Assert.False((await _s.Admin.PriceLists.DeleteAsync(Guid.NewGuid())).IsSuccess);

        Assert.Equal(before, await TotalRowsAsync());
        Assert.Equal([Expect(Admin, "price-list.created", list.Id, null, Header(name, false, null, 1, Added(sku, "PCS", "1.00")))], await TrailAsync(list.Id));
    }

    // ===== the cap of 50 lines =====
    [Fact]
    public async Task A_save_that_changes_sixty_prices_writes_the_first_fifty_in_the_order_added_removed_changed_and_counts_the_rest()
    {
        var skus = Enumerable.Range(0, 60).Select(i => Sku($"M{i:D2}")).ToList();
        var ids = new List<Guid>();
        foreach (var sku in skus) ids.Add((await ProductAsync(sku)).Id);
        var name = Unique.Name("Sixty");
        // 0..39 are in the list at 1.00; the save drops 0..19, reprices 20..39 and adds 40..59: 20 added, 20 removed, 20 changed
        var list = await _s.NewListAsync(Enumerable.Range(0, 40).Aggregate(_s.List(name), (b, i) => b.WithItem(ids[i], 1m)));
        var request = Enumerable.Range(20, 40).Aggregate(_s.List(name), (b, i) => b.WithItem(ids[i], i < 40 ? 2m : 5m));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, request.Build())).ShouldBeOk();

        var words = new List<string>();
        words.AddRange(Enumerable.Range(40, 20).Select(i => Added(skus[i], "PCS", "5.00")));
        words.AddRange(Enumerable.Range(0, 20).Select(i => Removed(skus[i], "PCS", "1.00")));
        words.AddRange(Enumerable.Range(20, 20).Select(i => Changed(skus[i], "PCS", "1.00", "2.00")));
        var updated = Assert.Single(await TrailAsync(list.Id), r => r.Action == "price-list.updated");
        Assert.Equal(Header(name, false, null, 40), updated.Before);
        Assert.Equal(Header(name, false, null, 40, [.. words.Take(50), "... and 10 more"]), updated.After);
    }

    [Fact]
    public async Task Fifty_changed_prices_are_all_in_words_fifty_one_are_fifty_and_a_count_of_one_and_a_delete_of_fifty_one_does_the_same_with_removed()
    {
        var skus = Enumerable.Range(0, 51).Select(i => Sku($"B{i:D2}")).ToList();
        var ids = new List<Guid>();
        foreach (var sku in skus) ids.Add((await ProductAsync(sku)).Id);
        var name = Unique.Name("Border");
        var list = await _s.NewListAsync(ids.Aggregate(_s.List(name), (b, id) => b.WithItem(id, 1m)));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, ids.Select((id, i) => (id, i)).Aggregate(_s.List(name), (b, x) => b.WithItem(x.id, x.i < 50 ? 2m : 1m)).Build())).ShouldBeOk();   // 50 changes
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, ids.Aggregate(_s.List(name), (b, id) => b.WithItem(id, 3m)).Build())).ShouldBeOk();                                          // 51 changes
        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(list.Id)).Status);

        var rows = await TrailAsync(list.Id);
        Assert.Equal(["price-list.created", "price-list.updated", "price-list.updated", "price-list.deleted"], rows.Select(r => r.Action));
        Assert.Equal(Header(name, false, null, 51, Enumerable.Range(0, 50).Select(i => Changed(skus[i], "PCS", "1.00", "2.00")).ToArray()), rows[1].After);
        Assert.Equal(Header(name, false, null, 51, [.. Enumerable.Range(0, 50).Select(i => Changed(skus[i], "PCS", "2.00", "3.00")), "... and 1 more"]), rows[2].After);
        Assert.Equal(Header(name, false, null, 51, [.. Enumerable.Range(0, 50).Select(i => Removed(skus[i], "PCS", "3.00")), "... and 1 more"]), rows[3].Before);
        Assert.Null(rows[3].After);
        Assert.Equal(Header(name, false, null, 51, [.. Enumerable.Range(0, 50).Select(i => Added(skus[i], "PCS", "1.00")), "... and 1 more"]), rows[0].After);
    }

    // ===== who sees the prices =====
    private ApiClient Token(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    [Fact]
    public async Task The_audit_read_of_a_caller_without_pricelist_manage_shows_no_price_in_any_row_of_the_list_and_cost_view_alone_never_opens_them()
    {
        var customer = await _s.NewPartnerAsync();
        var (a, b) = (Sku("V"), Sku("W"));
        var (pa, pb) = (await ProductAsync(a), await ProductAsync(b));
        var name = Unique.Name("Secret");
        var list = await _s.NewListAsync(_s.List(name).WithItem(pa.Id, 7777.77m).WithItem(pb.Id, 6543.21m));
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(pa.Id, 7000.50m).Build())).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id)).ShouldBeOk();
        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(list.Id)).Status);
        var id = await CustomerIdAsync(customer.Id);

        async Task<List<AuditEntry>> ReadAsync(params string[] permissions) =>
            (await Token(permissions).Audit.QueryAsync(entityType: AuditEntities.PriceList, entityId: list.Id.ToString())).ShouldBeOk().Data<AuditPage>().Items.Reverse().ToList();   // newest first on the page: oldest first here
        var costOnly = await ReadAsync(Permissions.AuditRead, Permissions.CostView);
        var blind = await ReadAsync(Permissions.AuditRead, Permissions.SoManage, Permissions.CostView, Permissions.ValuationView);
        var sighted = await ReadAsync(Permissions.AuditRead, Permissions.PricelistManage);
        var raw = await RowsAsync(list.Id);

        Assert.Equal(["price-list.created", "price-list.updated", "price-list.customer-set", "price-list.deleted"], raw.Select(r => r.Action));
        foreach (var reader in new[] { costOnly, blind })
        {
            Assert.Equal(raw.Select(r => r.Action), reader.Select(e => e.Action));
            foreach (var entry in reader)
                foreach (var secret in new[] { "ListPriceChanges", "7777.77", "6543.21", "7000.50", "added at", "removed", "changed from" })
                    Assert.False((entry.Before ?? "").Contains(secret) || (entry.After ?? "").Contains(secret), $"{entry.Action} shows {secret} to a caller without pricelist.manage: {entry.Before} / {entry.After}");

            // the other keys, as written
            Assert.Equal(
                [
                    (Admin, "price-list.created", null, Header(name, false, null, 2)),
                    (Admin, "price-list.updated", Header(name, false, null, 2), Header(name, false, null, 1)),
                    (Admin, "price-list.customer-set", CustomerOnly(null), CustomerOnly(id)),
                    (Admin, "price-list.deleted", Header(name, false, id, 1), null)
                ],
                reader.Select(e => (e.Actor, e.Action, e.Before, e.After)));
        }

        // The reader with pricelist.manage sees the rows as they are stored, byte for byte, the words included.
        Assert.Equal(raw.Select(r => (r.Actor, r.Action, r.Before, r.After)), sighted.Select(e => (e.Actor, e.Action, e.Before, e.After)));
        Assert.Equal(Header(name, false, null, 2, Added(a, "PCS", "7777.77"), Added(b, "PCS", "6543.21")), sighted[0].After);
        Assert.Equal(Header(name, false, null, 1, Removed(b, "PCS", "6543.21"), Changed(a, "PCS", "7777.77", "7000.50")), sighted[1].After);
        Assert.Equal(Header(name, false, id, 1, Removed(a, "PCS", "7000.50")), Assert.Single(sighted, e => e.Action == "price-list.deleted").Before);
    }
}
