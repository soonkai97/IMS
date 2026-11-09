using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.11 acceptance 1 on a real SQL Server: creating a list with items, reading it back, listing the lists, replacing the items of a list (a kept item keeps its row, the rest is added, repriced or
/// removed), deleting a list, and one audit event for each save with the prices that changed in words.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListCrudTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<int> CountAsync(string table) => fixture.Database.ScalarAsync<int>($"SELECT COUNT(*) FROM {table}");

    private Task<int> AuditCountAsync(string action, Guid id) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @id",
        ("@a", action), ("@t", AuditEntities.PriceList), ("@id", id.ToString()));

    private async Task<string> AuditBodyAsync(ApiClient reader, string action, Guid id) =>
        (await reader.Audit.QueryAsync(entityType: AuditEntities.PriceList, entityId: id.ToString(), action: action)).ShouldBeOk().Body;

    [Fact]
    public async Task A_caller_with_pricelist_manage_creates_the_default_list_with_items_priced_per_unit_and_gets_it_back_with_its_token()
    {
        var piece = await _s.NewProductAsync(b => b.WithName("Apple"));
        var boxed = await _s.NewProductAsync(b => b.WithName("Biscuit").WithConversion(fixture.Reference.CartonUnitId, 12m));
        var request = _s.List("Retail").Default()
            .WithItem(boxed.Id, 30m, fixture.Reference.CartonUnitId)   // a price per carton is a price per carton: nothing is derived from the price per piece
            .WithItem(boxed.Id, 3.1m)                                  // no unit means the base unit
            .WithItem(piece.Id, 2.5m, piece.BaseUomId).Build();

        var response = await _s.Admin.PriceLists.CreateAsync(request);
        var list = response.Data<PriceListDto>();

        Assert.Equal(("Retail", true, null), (list.Name, list.IsDefault, list.CustomerId));
        Assert.Equal([("Apple", "PCS", 2.5m), ("Biscuit", "CTN", 30m), ("Biscuit", "PCS", 3.1m)], list.Items.Select(i => (i.ProductName, i.UomCode, i.Price!.Value)));
        Assert.Equal(boxed.BaseUomId, list.Items[2].UomId);
        Assert.Equal($"\"{list.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.Equal((1, 3), (await CountAsync("PriceList"), await CountAsync("PriceListItem")));

        var read = await _s.Admin.PriceLists.GetAsync(list.Id);
        Assert.Equal(list.ConcurrencyToken, read.Data<PriceListDto>().ConcurrencyToken);
        Assert.Equal($"\"{list.ConcurrencyToken}\"", read.Header("ETag"));
    }

    [Fact]
    public async Task A_list_can_be_saved_with_no_items_and_a_list_that_does_not_exist_is_404()
    {
        var empty = await _s.NewListAsync(_s.List());

        Assert.Empty(empty.Items);
        _ = (await _s.Admin.PriceLists.GetAsync(Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_writes_exactly_one_audit_event_and_it_names_the_prices_in_words_for_a_reader_who_may_see_them()
    {
        var product = await _s.NewProductAsync(b => b.WithSku("AUD-1"));

        var list = await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 4.2m));

        Assert.Equal(1, await AuditCountAsync(AuditActions.PriceListCreated, list.Id));
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityType IN (N'PriceList', N'PriceListItem') AND Action IN (N'created', N'updated', N'deleted')")); // business events only, not one row per item
        var body = await AuditBodyAsync(_s.Admin, AuditActions.PriceListCreated, list.Id);
        Assert.Contains("AUD-1", body);
        Assert.Contains("added at 4.20", body);
    }

    [Fact]
    public async Task The_lists_come_default_first_then_by_name_with_their_item_counts_and_a_token_that_deletes_without_opening()
    {
        var product = await _s.NewProductAsync();
        var customer = await _s.NewPartnerAsync();
        await _s.NewListAsync(_s.List("Zulu"));
        await _s.NewListAsync(_s.List("Alpha").ForCustomer(customer.Id).WithItem(product.Id, 1m));
        var standard = await _s.NewListAsync(_s.List("Standard").Default().WithItem(product.Id, 2m));

        var page = (await _s.Admin.PriceLists.ListAsync()).Data<PagedResult<PriceListListItem>>();

        Assert.Equal(["Standard", "Alpha", "Zulu"], page.Items.Select(l => l.Name));
        Assert.Equal([1, 1, 0], page.Items.Select(l => l.ItemCount));
        Assert.Equal((customer.Id, customer.Name), (page.Items[1].CustomerId, page.Items[1].CustomerName));
        Assert.Equal(3, page.Total);
        Assert.Equal(standard.ConcurrencyToken, page.Items[0].ConcurrencyToken);

        Assert.Equal(["Alpha"], (await _s.Admin.PriceLists.ListAsync(search: "alp")).Data<PagedResult<PriceListListItem>>().Items.Select(l => l.Name));
        Assert.Equal(["Zulu"], (await _s.Admin.PriceLists.ListAsync(pageSize: 1, page: 3)).Data<PagedResult<PriceListListItem>>().Items.Select(l => l.Name));
    }

    [Fact]
    public async Task A_save_keeps_the_rows_of_the_items_it_keeps_and_adds_reprices_and_removes_the_rest()
    {
        var kept = await _s.NewProductAsync(b => b.WithSku("KEPT"));
        var repriced = await _s.NewProductAsync(b => b.WithSku("REPR"));
        var removed = await _s.NewProductAsync(b => b.WithSku("GONE"));
        var added = await _s.NewProductAsync(b => b.WithSku("NEWP"));
        var list = await _s.NewListAsync(_s.List("Wholesale").WithItem(kept.Id, 1m).WithItem(repriced.Id, 2m).WithItem(removed.Id, 3m));
        async Task<(int Id, decimal Price)> RowAsync(string sku) => (await fixture.Database.ScalarAsync<int>(
            "SELECT i.Id FROM PriceListItem i JOIN Product p ON p.Id = i.ProductId WHERE p.Sku = @s AND i.REC_DELETED = 0", ("@s", sku)), await fixture.Database.ScalarAsync<decimal>(
            "SELECT i.ListPrice FROM PriceListItem i JOIN Product p ON p.Id = i.ProductId WHERE p.Sku = @s AND i.REC_DELETED = 0", ("@s", sku)));
        var keptBefore = await RowAsync("KEPT");
        var repricedBefore = await RowAsync("REPR");

        var saved = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List("Wholesale 2").Default().WithItem(kept.Id, 1m).WithItem(repriced.Id, 2.75m).WithItem(added.Id, 9m).Build())).Data<PriceListDto>();

        Assert.Equal(("Wholesale 2", true), (saved.Name, saved.IsDefault));
        Assert.Equal(["KEPT", "NEWP", "REPR"], saved.Items.Select(i => i.Sku).Order());
        Assert.Equal(keptBefore, await RowAsync("KEPT"));                                          // the same row, untouched
        Assert.Equal((repricedBefore.Id, 2.75m), await RowAsync("REPR"));                          // the same row, repriced in place
        Assert.Equal(9m, (await RowAsync("NEWP")).Price);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem WHERE REC_DELETED = 1"));   // the removed item is soft-deleted
        Assert.NotEqual(list.ConcurrencyToken, saved.ConcurrencyToken);

        var words = await AuditBodyAsync(_s.Admin, AuditActions.PriceListUpdated, list.Id);
        Assert.Contains("REPR (PCS): changed from 2.00 to 2.75", words);
        Assert.Contains("NEWP (PCS): added at 9.00", words);
        Assert.Contains("GONE (PCS): removed (was 3.00)", words);
        Assert.DoesNotContain("KEPT (PCS):", words);
        Assert.Equal(1, await AuditCountAsync(AuditActions.PriceListUpdated, list.Id));
    }

    [Fact]
    public async Task A_save_that_changes_many_prices_writes_the_first_fifty_in_words_and_counts_the_rest()
    {
        var products = new List<Application.Features.Catalog.ProductDto>();
        for (var i = 0; i < 60; i++) products.Add(await _s.NewProductAsync());
        var list = await _s.NewListAsync(products.Aggregate(_s.List(), (b, p) => b.WithItem(p.Id, 1m)));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, products.Aggregate(_s.List(list.Name), (b, p) => b.WithItem(p.Id, 2m)).Build())).ShouldBeOk();

        var body = await AuditBodyAsync(_s.Admin, AuditActions.PriceListUpdated, list.Id);
        Assert.Equal(50, System.Text.RegularExpressions.Regex.Matches(body, "changed from 1.00 to 2.00").Count);
        Assert.Contains("... and 10 more", body);
        Assert.Equal(60, (await _s.Admin.PriceLists.GetAsync(list.Id)).Data<PriceListDto>().Items.Count);
    }

    [Fact]
    public async Task A_save_that_only_reprices_an_item_still_moves_the_token_of_the_list()
    {
        var product = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List().WithItem(product.Id, 1m));

        var saved = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).WithItem(product.Id, 1.5m).Build())).Data<PriceListDto>();

        Assert.NotEqual(list.ConcurrencyToken, saved.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).Build(), list.ConcurrencyToken)).Status);
    }

    [Fact]
    public async Task Deleting_a_list_soft_deletes_it_and_its_items_frees_its_name_the_default_and_the_customer_and_writes_one_audit_event()
    {
        var product = await _s.NewProductAsync();
        var customer = await _s.NewPartnerAsync();
        var theirs = await _s.NewListAsync(_s.List("Theirs").ForCustomer(customer.Id).WithItem(product.Id, 1m));
        var standard = await _s.NewListAsync(_s.List("Standard").Default().WithItem(product.Id, 2m));

        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(theirs.Id)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _s.Admin.PriceLists.DeleteAsync(standard.Id)).Status);

        _ = (await _s.Admin.PriceLists.GetAsync(theirs.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        _ = (await _s.Admin.PriceLists.DeleteAsync(theirs.Id, "stale")).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal((2, 2), (await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE REC_DELETED = 1"), await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem WHERE REC_DELETED = 1")));
        Assert.Empty((await _s.Admin.PriceLists.ListAsync()).Data<PagedResult<PriceListListItem>>().Items);
        Assert.Equal((1, 1), (await AuditCountAsync(AuditActions.PriceListDeleted, theirs.Id), await AuditCountAsync(AuditActions.PriceListDeleted, standard.Id)));

        // a deleted list frees what it held
        var again = await _s.NewListAsync(_s.List("Standard").Default());
        var customerAgain = await _s.NewListAsync(_s.List("Theirs").ForCustomer(customer.Id));
        Assert.Equal(2, (await _s.Admin.PriceLists.ListAsync()).Data<PagedResult<PriceListListItem>>().Total);
        Assert.NotEqual(standard.Id, again.Id);
        Assert.NotEqual(theirs.Id, customerAgain.Id);
    }
}
