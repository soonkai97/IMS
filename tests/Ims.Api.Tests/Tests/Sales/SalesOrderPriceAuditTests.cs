using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.12: <c>sales-order.price-changed</c> is written at SAVE, for a line whose price differs from the list price and that is new or repriced since the previous save. A re-save of unchanged lines,
/// a submit and a list edited in between write nothing; a line with no price yet or no list price writes nothing; a saved order never changes when a list does.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderPriceAuditTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<int> EventsAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderPriceChanged), ("@no", no));

    private SalesOrderBuilder Order(Guid customer, params (Guid Product, decimal Price)[] lines) =>
        lines.Aggregate(new SalesOrderBuilder(_s.Warehouse, customer), (b, l) => b.WithLine(l.Product, 1m, l.Price));

    private async Task<SalesOrderDto> CreateAsync(SalesOrderBuilder order) => (await _s.Admin.SalesOrders.CreateAsync(order.Build())).Data<SalesOrderDto>();

    [Fact]
    public async Task A_line_priced_differently_from_the_list_writes_one_event_naming_the_line_the_list_price_the_entered_price_and_the_source_and_the_others_write_none()
    {
        var customer = await _s.NewPartnerAsync();
        var same = await _s.NewProductAsync();
        var different = await _s.NewProductAsync();
        var unlisted = await _s.NewProductAsync();
        var unpriced = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().ForCustomer(customer.Id).WithItem(same.Id, 5m).WithItem(different.Id, 5m).WithItem(unpriced.Id, 5m));

        var order = await CreateAsync(Order(customer.Id, (same.Id, 5m), (different.Id, 4.25m), (unlisted.Id, 9m), (unpriced.Id, 0m)));

        Assert.Equal(1, await EventsAsync(order.No));
        var body = (await _s.Admin.Audit.QueryAsync(entityType: AuditEntities.SalesOrder, entityId: order.No, action: AuditActions.SalesOrderPriceChanged)).ShouldBeOk().Body;
        Assert.Matches(@"Line\\?""\s*:\s*2", body);
        Assert.Contains("ListedPrice", body);
        Assert.Contains("5", body);
        Assert.Contains("4.25", body);
        Assert.Contains("Customer", body);
    }

    [Fact]
    public async Task The_default_list_counts_when_the_customer_has_none_and_a_line_priced_like_the_list_writes_nothing()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 8m));

        var equal = await CreateAsync(Order(customer.Id, (product.Id, 8m)));
        var changed = await CreateAsync(Order(customer.Id, (product.Id, 7m)));

        Assert.Equal((0, 1), (await EventsAsync(equal.No), await EventsAsync(changed.No)));
    }

    [Fact]
    public async Task Saving_again_unchanged_and_submitting_repeat_nothing_and_a_list_edited_in_between_adds_nothing_but_a_repriced_line_adds_one()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List().ForCustomer(customer.Id).WithItem(product.Id, 10m));
        var order = await CreateAsync(Order(customer.Id, (product.Id, 9m)));
        Assert.Equal(1, await EventsAsync(order.No));

        var again = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Order(customer.Id, (product.Id, 9m)).Build())).Data<SalesOrderDto>();
        Assert.Equal(1, await EventsAsync(order.No));                                      // the same line saved again: no second event

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).ForCustomer(customer.Id).WithItem(product.Id, 20m).Build())).ShouldBeOk(); // the list changes in between
        var third = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Order(customer.Id, (product.Id, 9m)).Build())).Data<SalesOrderDto>();
        Assert.Equal(1, await EventsAsync(order.No));                                      // the line is unchanged since the previous save: nothing, whatever the list says now

        _ = (await _s.Admin.SalesOrders.SubmitAsync(third.Id, third.ConcurrencyToken)).ShouldBeOk();
        Assert.Equal(1, await EventsAsync(order.No));                                      // a submit writes none

        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_repriced_line_a_new_line_and_a_change_of_customer_each_write_their_event()
    {
        var customer = await _s.NewPartnerAsync();
        var other = await _s.NewPartnerAsync();
        var a = await _s.NewProductAsync();
        var b = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().Default().WithItem(a.Id, 10m).WithItem(b.Id, 10m));
        var order = await CreateAsync(Order(customer.Id, (a.Id, 10m)));
        Assert.Equal(0, await EventsAsync(order.No));

        _ = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Order(customer.Id, (a.Id, 9m)).Build())).ShouldBeOk();                 // repriced
        Assert.Equal(1, await EventsAsync(order.No));
        _ = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Order(customer.Id, (a.Id, 9m), (b.Id, 8m)).Build())).ShouldBeOk();     // the old line is the same, the new one differs
        Assert.Equal(2, await EventsAsync(order.No));
        _ = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Order(other.Id, (a.Id, 9m), (b.Id, 8m)).Build())).ShouldBeOk();       // another customer: both lines are new to it
        Assert.Equal(4, await EventsAsync(order.No));
    }

    [Fact]
    public async Task A_saved_order_keeps_its_price_when_the_list_changes_or_is_deleted()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        var list = await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 10m));
        var order = await CreateAsync(Order(customer.Id, (product.Id, 7m)));

        _ = (await _s.Admin.PriceLists.UpdateAsync(list.Id, _s.List(list.Name).Default().WithItem(product.Id, 99m).Build())).ShouldBeOk();
        _ = (await _s.Admin.PriceLists.DeleteAsync(list.Id)).ShouldBe(System.Net.HttpStatusCode.NoContent);

        var read = await _s.ReadAsync(order.Id);
        Assert.Equal(7m, Assert.Single(read.Lines).UnitPrice);
        Assert.Equal(order.ConcurrencyToken, read.ConcurrencyToken);
    }

    [Fact]
    public async Task The_event_shows_its_prices_to_a_reader_with_so_manage_and_hides_them_from_one_with_cost_view_only()
    {
        var customer = await _s.NewPartnerAsync();
        var product = await _s.NewProductAsync();
        await _s.NewListAsync(_s.List().Default().WithItem(product.Id, 123.45m));
        var order = await CreateAsync(Order(customer.Id, (product.Id, 67.89m)));
        async Task<string> BodyAsync(params string[] permissions) => (await fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions)).Audit
            .QueryAsync(entityType: AuditEntities.SalesOrder, entityId: order.No, action: AuditActions.SalesOrderPriceChanged)).ShouldBeOk().Body;

        var sighted = await BodyAsync(Permissions.AuditRead, Permissions.SoManage);
        var blind = await BodyAsync(Permissions.AuditRead, Permissions.CostView, Permissions.ValuationView, Permissions.PricelistManage);

        Assert.Contains("123.45", sighted);
        Assert.Contains("67.89", sighted);
        Assert.Contains(AuditActions.SalesOrderPriceChanged, blind);
        Assert.DoesNotContain("123.45", blind);
        Assert.DoesNotContain("67.89", blind);
        Assert.DoesNotContain("ListedPrice", blind);
        Assert.DoesNotContain("EnteredPrice", blind);
    }
}
