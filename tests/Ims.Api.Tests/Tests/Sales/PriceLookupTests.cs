using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.12: the price an order line is prefilled with. Per item: the customer's list item for the product and unit, else the default list's item for that product and unit, else none; a unit is exact;
/// a deleted list is not asked; the endpoint needs so.manage and not pricelist.manage.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceLookupTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private async Task<PriceLookupDto> LookupAsync(Guid customer, Guid product, Guid? uom = null, ApiClient? by = null) =>
        (await (by ?? _s.Admin).PriceLists.LookupAsync(customer, product, uom)).Data<PriceLookupDto>();

    private ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    [Fact]
    public async Task The_customers_item_wins_the_default_list_fills_in_per_item_and_nothing_found_is_a_200_with_no_price()
    {
        var customer = await _s.NewPartnerAsync();
        var both = await _s.NewProductAsync();
        var onlyDefault = await _s.NewProductAsync();
        var onlyCustomer = await _s.NewProductAsync();
        var neither = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List("Default").Default().WithItem(both.Id, 4m).WithItem(onlyDefault.Id, 3m));
        await _s.NewListAsync(_s.List("Theirs").ForCustomer(customer.Id).WithItem(both.Id, 5m).WithItem(onlyCustomer.Id, 6m));

        Assert.Equal((true, 5m, PriceSource.Customer), Parts(await LookupAsync(customer.Id, both.Id)));
        Assert.Equal((true, 3m, PriceSource.Default), Parts(await LookupAsync(customer.Id, onlyDefault.Id)));   // the customer's list has no item for it: the default's is used
        Assert.Equal((true, 6m, PriceSource.Customer), Parts(await LookupAsync(customer.Id, onlyCustomer.Id)));
        var none = await _s.Admin.PriceLists.LookupAsync(customer.Id, neither.Id);
        Assert.Equal((false, null, null), Parts(none.Data<PriceLookupDto>()));
        Assert.DoesNotContain("price", none.Body);
        var another = await _s.NewPartnerAsync();                                                                // a customer with no list gets the default's prices
        Assert.Equal((true, 4m, PriceSource.Default), Parts(await LookupAsync(another.Id, both.Id)));
        Assert.Equal((false, null, null), Parts(await LookupAsync(another.Id, onlyCustomer.Id)));
    }

    private static (bool, decimal?, PriceSource?) Parts(PriceLookupDto d) => (d.Found, d.Price, d.Source);

    [Fact]
    public async Task A_price_is_for_exactly_its_unit_and_no_unit_means_the_base_unit()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 12m));
        await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 30m, fixture.Reference.CartonUnitId));

        Assert.Equal((true, 30m, PriceSource.Default), Parts(await LookupAsync(customer.Id, product.Id, fixture.Reference.CartonUnitId)));
        Assert.False((await LookupAsync(customer.Id, product.Id)).Found);                                       // base unit: no item, nothing is derived from the carton price
        Assert.False((await LookupAsync(customer.Id, product.Id, product.BaseUomId)).Found);
        Assert.False((await LookupAsync(customer.Id, product.Id, Guid.NewGuid())).Found);                       // a unit that does not exist has no price
    }

    [Fact]
    public async Task A_deleted_default_list_and_a_deleted_customer_list_are_not_asked_even_though_their_item_rows_are_still_in_the_table()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        var standard = await _s.NewListAsync(_s.List("Default").Default().WithItem(product.Id, 4m));
        var theirs = await _s.NewListAsync(_s.List("Theirs").ForCustomer(customer.Id).WithItem(product.Id, 5m));
        // the shape that must not leak: the list is deleted but an item row is live (REC_DELETED = 0)
        async Task ReviveItemsAsync() => await fixture.Database.ExecuteAsync("UPDATE PriceListItem SET REC_DELETED = 0");

        _ = (await _s.Admin.PriceLists.DeleteAsync(theirs.Id)).ShouldBe(HttpStatusCode.NoContent);
        await ReviveItemsAsync();
        Assert.Equal((true, 4m, PriceSource.Default), Parts(await LookupAsync(customer.Id, product.Id)));      // the customer's deleted list is not used

        _ = (await _s.Admin.PriceLists.DeleteAsync(standard.Id)).ShouldBe(HttpStatusCode.NoContent);
        await ReviveItemsAsync();
        Assert.False((await LookupAsync(customer.Id, product.Id)).Found);                                       // nor the deleted default
    }

    [Fact]
    public async Task A_supplier_and_an_inactive_customer_are_a_400_with_the_generic_partner_sentence()
    {
        var product = await _s.NewProductAsync();
        var supplier = await _s.NewPartnerAsync(PartnerType.Supplier);
        var inactive = await _s.NewPartnerAsync(isActive: false);
        await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 4m));

        var forSupplier = (await _s.Admin.PriceLists.LookupAsync(supplier.Id, product.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var forInactive = (await _s.Admin.PriceLists.LookupAsync(inactive.Id, product.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal((Messages.PartnerMissing, Messages.PartnerMissing), (forSupplier.Detail, forInactive.Detail)); // the generic sentence: the lookup says nothing about partners
        (await _s.Admin.SalesOrders.CreateAsync(_s.Request(_s.Warehouse, product.Id, supplier.Id).Build())).ShouldBeProblem(HttpStatusCode.BadRequest); // while the save still refuses them in its own words
    }

    [Fact]
    public async Task A_customer_or_product_that_does_not_exist_is_a_400()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();

        (await _s.Admin.PriceLists.LookupAsync(Guid.NewGuid(), product.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Admin.PriceLists.LookupAsync(customer.Id, Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Admin.PriceLists.LookupAsync(Guid.Empty, product.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    // ----- who may ask -----
    [Fact]
    public async Task So_manage_alone_gets_the_price_and_pricelist_manage_so_read_and_cost_view_do_not_open_the_endpoint()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 4.5m));

        Assert.Equal(4.5m, (await LookupAsync(customer.Id, product.Id, by: Forged(Permissions.SoManage))).Price);
        foreach (var forged in new[] { Forged(Permissions.PricelistManage), Forged(Permissions.SoRead, Permissions.CostView, Permissions.ValuationView), Forged() })
            (await forged.PriceLists.LookupAsync(customer.Id, product.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(4.5m, (await LookupAsync(customer.Id, product.Id, by: _s.Clerk)).Price);                   // the clerk edits orders but not lists
        (await _s.Staff.PriceLists.LookupAsync(customer.Id, product.Id)).ShouldBeProblem(HttpStatusCode.Forbidden); // warehouse staff only read orders
    }

    [Fact]
    public async Task While_the_module_is_off_the_lookup_is_404_and_without_a_token_401()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.TurnOffAsync();

        (await _s.Admin.PriceLists.LookupAsync(customer.Id, product.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal(401, (await fixture.Anonymous().PriceLists.LookupAsync(customer.Id, product.Id)).StatusCode);
    }
}
