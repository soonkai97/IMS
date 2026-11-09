using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.11: a change or a delete needs the token the client last saw (428 without, 409 when stale), two requests racing for the one default, one customer or one name leave exactly one winner
/// and a sentence for the loser, and the database says the same things itself, so no write path can break them.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListConcurrencyTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    [Fact]
    public async Task A_save_a_customer_change_and_a_delete_without_if_match_are_428_and_change_nothing()
    {
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List());

        _ = (await _s.Admin.PriceLists.UpdateWithoutIfMatchAsync(list.Id, _s.List().Build())).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id, "*")).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        _ = (await _s.Admin.PriceLists.DeleteWithoutIfMatchAsync(list.Id)).ShouldBeProblem(HttpStatusCode.PreconditionRequired);

        var read = (await _s.Admin.PriceLists.GetAsync(list.Id)).Data<PriceListDto>();
        Assert.Equal((list.Name, null, list.ConcurrencyToken), (read.Name, read.CustomerId, read.ConcurrencyToken));
    }

    [Fact]
    public async Task A_stale_token_is_409_for_a_save_a_customer_change_and_a_delete_and_a_garbled_one_is_the_same_answer()
    {
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List());
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name + " v2").Build())).ShouldBeOk(); // somebody else changed it

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List().Build(), list.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id, list.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        _ = (await _s.Admin.PriceLists.DeleteAsync(list.Id, list.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        _ = (await _s.Admin.PriceLists.DeleteAsync(list.Id, "not-a-token")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE PublicId = @id AND REC_DELETED = 0", ("@id", list.Id)));
    }

    [Fact]
    public async Task A_customer_change_moves_the_token_so_the_save_that_was_open_before_it_is_refused()
    {
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List());

        var changed = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id)).Data<PriceListDto>();

        Assert.NotEqual(list.ConcurrencyToken, changed.ConcurrencyToken);
        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).Build(), list.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Racing_creates_of_the_default_list_leave_exactly_one_default_and_the_others_get_409()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => _s.Admin.PriceLists.CreateAsync(_s.List($"Default {i}").Default().Build())));

        Assert.Single(results, r => r.Status == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE IsDefault = 1 AND REC_DELETED = 0"));
    }

    [Fact]
    public async Task Racing_creates_for_one_customer_and_for_one_name_leave_exactly_one_winner()
    {
        var customer = await _s.NewPartnerAsync();

        var forCustomer = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => _s.Admin.PriceLists.CreateAsync(_s.List($"Customer {i}").ForCustomer(customer.Id).Build())));
        var sameName = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _s.Admin.PriceLists.CreateAsync(_s.List("Same name").Build())));

        Assert.Single(forCustomer, r => r.Status == HttpStatusCode.OK);
        Assert.Single(sameName, r => r.Status == HttpStatusCode.OK);
        Assert.All(forCustomer.Concat(sameName).Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        Assert.Equal(2, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE REC_DELETED = 0"));
    }

    [Fact]
    public async Task Racing_saves_with_one_token_let_one_through_and_refuse_the_rest()
    {
        var product = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List("Racing").WithItem(product.Id, 1m));

        var results = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List("Racing").WithItem(product.Id, i + 1).Build(), list.ConcurrencyToken)));

        Assert.Single(results, r => r.Status == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem WHERE REC_DELETED = 0")); // one live item, whoever won
    }

    // ----- the database says it too -----
    [Fact]
    public async Task The_database_refuses_a_second_default_a_second_list_for_a_customer_a_second_price_and_the_values_the_domain_refuses()
    {
        var product = await _s.NewProductAsync();
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 1m));
        var other = await _s.NewListAsync(_s.List().ForCustomer(customer.Id));
        var keys = (list: await fixture.Database.ScalarAsync<int>("SELECT Id FROM PriceList WHERE PublicId = @id", ("@id", list.Id)),
            customer: await fixture.Database.ScalarAsync<int>("SELECT Id FROM Partner WHERE PublicId = @id", ("@id", customer.Id)),
            product: await fixture.Database.ScalarAsync<int>("SELECT Id FROM Product WHERE PublicId = @id", ("@id", product.Id)),
            uom: await fixture.Database.ScalarAsync<int>("SELECT Id FROM Uom WHERE PublicId = @id", ("@id", product.BaseUomId)));

        async Task<int> FailsWithAsync(string sql) => (await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync(sql, ("@list", keys.list), ("@customer", keys.customer), ("@product", keys.product), ("@uom", keys.uom)))).Number;

        Assert.Equal(2601, await FailsWithAsync("INSERT PriceList (Name, IsDefault) VALUES (N'Second default', 1)"));
        Assert.Equal(2601, await FailsWithAsync("INSERT PriceList (Name, IsDefault, CustomerId) VALUES (N'Second for customer', 0, @customer)"));
        Assert.Equal(2601, await FailsWithAsync($"INSERT PriceList (Name, IsDefault) VALUES (N'{other.Name.ToUpperInvariant()}', 0)"));
        Assert.Equal(2601, await FailsWithAsync("INSERT PriceListItem (PriceListId, ProductId, UomId, ListPrice) VALUES (@list, @product, @uom, 9)"));
        Assert.Equal(547, await FailsWithAsync("INSERT PriceListItem (PriceListId, ProductId, UomId, ListPrice) SELECT TOP (1) @list, @product, Id, 0 FROM Uom WHERE Id <> @uom"));
        Assert.Equal(547, await FailsWithAsync("UPDATE PriceListItem SET ListPrice = -1 WHERE PriceListId = @list"));
    }

    [Fact]
    public async Task The_database_refuses_to_make_a_customers_list_the_default_even_when_no_default_exists()
    {
        var customer = await _s.NewPartnerAsync();
        await _s.NewListAsync(_s.List().ForCustomer(customer.Id));

        var error = await Assert.ThrowsAsync<SqlException>(() => fixture.Database.ExecuteAsync("UPDATE PriceList SET IsDefault = 1 WHERE CustomerId IS NOT NULL"));

        Assert.Equal(547, error.Number);
    }

    [Fact]
    public async Task A_deleted_row_does_not_hold_the_default_the_customer_the_name_or_the_product_in_the_database_either()
    {
        var product = await _s.NewProductAsync();
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List("Held").Default().WithItem(product.Id, 1m));
        var theirs = await _s.NewListAsync(_s.List("Theirs").ForCustomer(customer.Id));
        _ = (await _s.Admin.PriceLists.DeleteAsync(list.Id)).ShouldBe(HttpStatusCode.NoContent);
        _ = (await _s.Admin.PriceLists.DeleteAsync(theirs.Id)).ShouldBe(HttpStatusCode.NoContent);

        // the default, the name, the customer and the item are all free again, straight in the database
        Assert.Equal(1, await fixture.Database.ExecuteAsync("INSERT PriceList (Name, IsDefault) VALUES (N'Held', 1)"));
        Assert.Equal(1, await fixture.Database.ExecuteAsync("INSERT PriceList (Name, IsDefault, CustomerId) SELECT N'Theirs', 0, Id FROM Partner WHERE PublicId = @id", ("@id", customer.Id)));
        Assert.Equal(1, await fixture.Database.ExecuteAsync("INSERT PriceListItem (PriceListId, ProductId, UomId, ListPrice) SELECT (SELECT Id FROM PriceList WHERE Name = N'Held' AND REC_DELETED = 0), p.Id, p.BaseUomId, 1 FROM Product p WHERE p.PublicId = @id", ("@id", product.Id)));
    }
}
