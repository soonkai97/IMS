using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.11 acceptance 1 and 2: one default list (a second is 409), at most one list per customer, a supplier cannot have one, one name, a product priced once in each unit, and every way a
/// price or an item can be wrong, each answered with the field it belongs to and all items at once. A refused request writes nothing.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListRuleTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<int> CountAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList");

    private async Task<ProblemBody> RefusedAsync(PriceListSaveRequest request, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        var before = (await CountAsync(), await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem"));
        var problem = (await _s.Admin.PriceLists.CreateAsync(request)).ShouldBeProblem(status);
        Assert.Equal(before, (await CountAsync(), await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceListItem"))); // a refused request writes nothing
        return problem;
    }

    // ----- the default list -----
    [Fact]
    public async Task A_second_default_list_is_409_and_names_the_one_that_is_the_default()
    {
        await _s.NewListAsync(_s.List("Standard").Default());

        var problem = await RefusedAsync(_s.List("Another").Default().Build(), HttpStatusCode.Conflict);

        Assert.Equal(Messages.PriceListDefaultExists("Standard"), problem.Detail);
    }

    [Fact]
    public async Task A_list_becomes_the_default_only_after_the_old_default_gave_it_up_and_a_list_can_save_itself_as_the_default()
    {
        var old = await _s.NewListAsync(_s.List("Old").Default());
        var next = await _s.NewListAsync(_s.List("Next"));

        _ = (await _s.Admin.PriceLists.UpdateAsync(next.Id, _s.List("Next").Default().Build())).ShouldBeProblem(HttpStatusCode.Conflict);
        _ = (await _s.Admin.PriceLists.UpdateAsync(old.Id, _s.List("Old").Default().Build())).ShouldBeOk();   // saving the default as the default again is no conflict with itself
        _ = (await _s.Admin.PriceLists.UpdateAsync(old.Id, _s.List("Old").Build())).ShouldBeOk();
        var promoted = (await _s.Admin.PriceLists.UpdateAsync(next.Id, _s.List("Next").Default().Build())).Data<PriceListDto>();

        Assert.True(promoted.IsDefault);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE IsDefault = 1 AND REC_DELETED = 0"));
    }

    [Fact]
    public async Task The_default_list_is_for_everybody_so_it_cannot_belong_to_a_customer_and_a_customer_list_cannot_become_the_default()
    {
        var customer = await _s.NewPartnerAsync();
        var theirs = await _s.NewListAsync(_s.List().ForCustomer(customer.Id));
        var standard = await _s.NewListAsync(_s.List().Default());

        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, (await RefusedAsync(_s.List().Default().ForCustomer(customer.Id).Build())).Detail);
        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, (await _s.Admin.PriceLists.SetCustomerAsync(standard.Id, customer.Id)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PriceListDefaultHasNoCustomer, (await _s.Admin.PriceLists.UpdateAsync(theirs.Id, _s.List(theirs.Name).Default().ForCustomer(customer.Id).Build())).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    // ----- the customer -----
    [Fact]
    public async Task A_customer_has_at_most_one_list_and_the_second_is_409_naming_the_first()
    {
        var customer = await _s.NewPartnerAsync();
        var first = await _s.NewListAsync(_s.List("First").ForCustomer(customer.Id));
        var second = await _s.NewListAsync(_s.List("Second"));

        var created = await RefusedAsync(_s.List("Third").ForCustomer(customer.Id).Build(), HttpStatusCode.Conflict);
        var updated = (await _s.Admin.PriceLists.UpdateAsync(second.Id, _s.List("Second").ForCustomer(customer.Id).Build())).ShouldBeProblem(HttpStatusCode.Conflict);
        var set = (await _s.Admin.PriceLists.SetCustomerAsync(second.Id, customer.Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        var expected = Messages.PriceListCustomerHasList(customer.Name, first.Name);
        Assert.Equal([expected, expected, expected], [created.Detail, updated.Detail, set.Detail]);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE CustomerId IS NOT NULL"));
    }

    [Fact]
    public async Task A_supplier_an_inactive_customer_and_a_partner_that_does_not_exist_cannot_have_a_list()
    {
        var supplier = await _s.NewPartnerAsync(PartnerType.Supplier);
        var inactive = await _s.NewPartnerAsync(isActive: false);
        var list = await _s.NewListAsync(_s.List());

        Assert.Equal(Messages.PriceListCustomerRequiredType, (await RefusedAsync(_s.List().ForCustomer(supplier.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerInactive, (await RefusedAsync(_s.List().ForCustomer(inactive.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerMissing, (await RefusedAsync(_s.List().ForCustomer(Guid.NewGuid()).Build())).Detail);
        Assert.Equal(Messages.PriceListCustomerRequiredType, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, supplier.Id)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerInactive, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, inactive.Id)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerMissing, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.ChooseCustomer, (await RefusedAsync(_s.List().ForCustomer(Guid.Empty).Build())).ErrorsFor("customerId").Single());
    }

    [Fact]
    public async Task Setting_the_customer_gives_the_list_moves_it_and_takes_it_off_without_touching_the_items_and_writes_one_audit_event_each_time()
    {
        var product = await _s.NewProductAsync();
        var one = await _s.NewPartnerAsync();
        var two = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List().WithItem(product.Id, 5m));

        var given = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, one.Id)).Data<PriceListDto>();
        var moved = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, two.Id)).Data<PriceListDto>();
        var freed = (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, null)).Data<PriceListDto>();
        var oneAgain = await _s.NewListAsync(_s.List().ForCustomer(one.Id)); // the first customer is free again

        Assert.Equal([(one.Id, one.Name), (two.Id, two.Name)], [(given.CustomerId!.Value, given.CustomerName!), (moved.CustomerId!.Value, moved.CustomerName!)]);
        Assert.Null(freed.CustomerId);
        Assert.All(new[] { given, moved, freed }, l => Assert.Equal(5m, Assert.Single(l.Items).Price));
        Assert.Equal(3, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @id", ("@a", AuditActions.PriceListCustomerSet), ("@id", list.Id.ToString())));
        Assert.NotEqual(list.ConcurrencyToken, given.ConcurrencyToken);
        Assert.NotNull(oneAgain);
    }

    [Fact]
    public async Task A_null_customer_takes_the_list_off_its_customer_and_an_empty_id_or_an_unknown_list_is_refused()
    {
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List().ForCustomer(customer.Id));

        Assert.Equal(Messages.ChooseCustomer, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, Guid.Empty)).ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("customerId").Single());
        Assert.Null((await _s.Admin.PriceLists.SetCustomerAsync(list.Id, null)).Data<PriceListDto>().CustomerId);
        _ = (await _s.Admin.PriceLists.SetCustomerAsync(Guid.NewGuid(), customer.Id, "x")).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    // ----- the name -----
    [Fact]
    public async Task A_name_is_required_trimmed_at_most_200_characters_and_unique_among_the_lists_whatever_its_case()
    {
        var made = await _s.NewListAsync(_s.List("  Retail  "));
        var other = await _s.NewListAsync(_s.List("Other"));

        Assert.Equal("Retail", made.Name);
        Assert.Equal(Messages.PriceListNameTaken("RETAIL"), (await RefusedAsync(_s.List("RETAIL").Build(), HttpStatusCode.Conflict)).Detail);
        Assert.Equal(Messages.PriceListNameTaken("retail"), (await _s.Admin.PriceLists.UpdateAsync(other.Id, _s.List("retail").Build())).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        _ = (await _s.Admin.PriceLists.UpdateAsync(made.Id, _s.List("RETAIL").Build())).ShouldBeOk(); // a list keeps its own name, in any case
        Assert.Contains(Messages.PriceListNameRequired, (await RefusedAsync(_s.List("  ").Build())).ErrorsFor("name"));
        Assert.Contains(Messages.PriceListNameTooLong, (await RefusedAsync(_s.List(new string('x', Limits.ShortNameMaxLength + 1)).Build())).ErrorsFor("name"));
    }

    // ----- the items -----
    [Fact]
    public async Task A_product_is_priced_once_in_each_unit_and_the_base_unit_by_name_is_the_base_unit_by_default()
    {
        var product = await _s.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 12m));

        var twice = await RefusedAsync(_s.List().WithItem(product.Id, 1m).WithItem(product.Id, 2m, product.BaseUomId).Build());
        var cartons = await _s.NewListAsync(_s.List().WithItem(product.Id, 1m).WithItem(product.Id, 12m, fixture.Reference.CartonUnitId));

        Assert.Contains(Messages.PriceListDuplicateItem(product.Sku, product.BaseUomCode), twice.ErrorsFor("items[1]"));
        Assert.Equal(2, cartons.Items.Count);
    }

    [Fact]
    public async Task Every_item_that_is_wrong_is_reported_with_its_own_field_in_one_answer()
    {
        var product = await _s.NewProductAsync();

        var problem = await RefusedAsync(_s.List()
            .WithItem(product.Id, 0m)                                            // 0
            .WithItem(product.Id, 1.005m, product.BaseUomId)                     // 3 decimals
            .WithItem(product.Id, Limits.MaxUnitCost + 1)                        // too large
            .WithItem(Guid.Empty, 1m)
            .Build());

        Assert.Contains(Messages.PriceListItemPricePositive, problem.ErrorsFor("items[0].price"));
        Assert.Contains(Messages.PriceTooPrecise, problem.ErrorsFor("items[1].price"));
        Assert.Contains(Messages.PriceTooLarge, problem.ErrorsFor("items[2].price"));
        Assert.Contains(Messages.ChooseProduct, problem.ErrorsFor("items[3].productId"));
    }

    [Fact]
    public async Task A_unit_the_product_cannot_convert_an_unknown_product_and_an_inactive_product_are_refused_item_by_item()
    {
        var product = await _s.NewProductAsync();
        var inactive = await _s.NewProductAsync(b => b.Inactive());

        var problem = await RefusedAsync(_s.List()
            .WithItem(product.Id, 1m, fixture.Reference.KilogramUnitId)
            .WithItem(Guid.NewGuid(), 1m)
            .WithItem(inactive.Id, 1m)
            .WithItem(product.Id, 1m, Guid.NewGuid())
            .Build());

        Assert.Equal(["items[0]", "items[1]", "items[2]", "items[3]"], problem.Errors!.Keys.Select(k => k.Split('.').Last()).Order());
    }

    [Fact]
    public async Task A_price_of_exactly_the_largest_amount_and_of_one_cent_is_accepted()
    {
        var a = await _s.NewProductAsync();
        var b = await _s.NewProductAsync();

        var list = await _s.NewListAsync(_s.List().WithItem(a.Id, 0.01m).WithItem(b.Id, Limits.MaxUnitCost));

        Assert.Equal([0.01m, Limits.MaxUnitCost], list.Items.Select(i => i.Price!.Value).Order());
    }

    [Fact]
    public async Task A_list_holds_at_most_the_largest_number_of_items()
    {
        var product = await _s.NewProductAsync();
        var items = Enumerable.Range(0, Limits.MaxPriceListItems + 1).Select(_ => new PriceListItemInput(product.Id, null, 1m)).ToList();

        var problem = await RefusedAsync(new PriceListSaveRequest(Support.Unique.Name("Big"), false, null, items));

        Assert.Contains(Messages.PriceListTooManyItems(Limits.MaxPriceListItems), problem.ErrorsFor("items"));
    }

    // ----- what was fine when it was saved stays savable (PL3.12) -----
    [Fact]
    public async Task A_list_with_an_item_whose_product_was_deactivated_since_can_still_be_saved_but_a_new_item_for_an_inactive_product_is_refused()
    {
        var kept = await _s.NewProductAsync();
        var gone = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List().WithItem(kept.Id, 1m).WithItem(gone.Id, 2m));
        await fixture.Database.ExecuteAsync("UPDATE Product SET IsActive = 0 WHERE PublicId = @id", ("@id", gone.Id));

        var saved = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).WithItem(kept.Id, 1.5m).WithItem(gone.Id, 2m).Build())).Data<PriceListDto>();   // the old item stays, even repriced
        Assert.Equal([1.5m, 2m], saved.Items.Select(i => i.Price!.Value).Order());

        var fresh = await _s.NewProductAsync();
        await fixture.Database.ExecuteAsync("UPDATE Product SET IsActive = 0 WHERE PublicId = @id", ("@id", fresh.Id));
        var refused = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).WithItem(kept.Id, 1.5m).WithItem(gone.Id, 2m).WithItem(fresh.Id, 3m).Build())).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.NotEmpty(refused.ErrorsFor("items[2]"));
    }

    [Fact]
    public async Task A_list_of_a_customer_who_was_deactivated_since_can_still_be_saved_but_giving_it_to_another_inactive_customer_is_refused()
    {
        var customer = await _s.NewPartnerAsync();
        var list = await _s.NewListAsync(_s.List().ForCustomer(customer.Id));
        await fixture.Database.ExecuteAsync("UPDATE Partner SET IsActive = 0 WHERE PublicId = @id", ("@id", customer.Id));
        var inactive = await _s.NewPartnerAsync(isActive: false);

        var renamed = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List("Renamed").ForCustomer(customer.Id).Build())).Data<PriceListDto>();
        Assert.Equal(("Renamed", customer.Id), (renamed.Name, renamed.CustomerId!.Value));
        Assert.Equal(customer.Id, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, customer.Id)).Data<PriceListDto>().CustomerId);   // setting the same customer again

        Assert.Equal(Messages.PartnerInactive, (await _s.Admin.PriceLists.SetCustomerAsync(list.Id, inactive.Id)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerInactive, (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List("Renamed").ForCustomer(inactive.Id).Build())).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }
}
