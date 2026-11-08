using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>PL3.2 acceptance 4: saving a draft. Lines are replaced, number and status kept, the header is required (428) and checked (409), a draft only is editable, and the row version moves on a lines-only change.</summary>
[Collection(ApiCollection.Name)]
public class SalesOrderUpdateTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;
    private WarehouseDto _warehouse = null!;
    private ProductDto _product = null!;

    public async Task InitializeAsync()
    {
        _s = await SalesScenario.CreateAsync(fixture);
        _warehouse = await _s.NewWarehouseAsync();
        _product = await _s.NewProductAsync();
    }

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private SalesOrderBuilder Edited(string remark = "edited") => new SalesOrderBuilder(_warehouse.Id, fixture.Reference.CustomerId).WithRemark(remark);

    private Task<SalesOrderDto> Created() => _s.CreateAsync(_warehouse.Id, _product.Id);

    private async Task<SalesOrderDto> ReadAsync(SalesOrderDto order) => (await _s.Admin.SalesOrders.GetAsync(order.Id)).Data<SalesOrderDto>();

    [Fact]
    public async Task A_save_replaces_the_header_and_all_lines_and_keeps_the_number_and_the_status()
    {
        var order = await Created();
        var other = await _s.NewProductAsync();

        var response = await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited().WithLine(other.Id, 4m, 12.5m).WithLine(_product.Id, 1m, 0.5m).Build(), order.ConcurrencyToken);
        var saved = response.Data<SalesOrderDto>();

        Assert.Equal((order.No, SalesOrderStatus.Draft, "edited"), (saved.No, saved.Status, saved.Remark));
        Assert.Equal([other.Id, _product.Id], saved.Lines.Select(l => l.ProductId));
        Assert.Equal([1, 2], saved.Lines.Select(l => l.LineNo));
        Assert.Equal(50.5m, saved.TotalAmount);
        Assert.NotEqual(order.ConcurrencyToken, saved.ConcurrencyToken); // a lines-only change still moves the row version
        Assert.Equal($"\"{saved.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.Equal(2, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrderLine l JOIN SalesOrder o ON o.Id = l.SalesOrderId WHERE o.PublicId = @id AND l.REC_DELETED = 0", ("@id", order.Id)));
    }

    [Fact]
    public async Task Any_holder_of_so_manage_may_edit_any_draft_not_only_their_own()
    {
        var order = await _s.CreateAsync(fixture.Reference.DefaultWarehouseId, _product.Id, by: _s.Clerk);

        var request = new SalesOrderBuilder(fixture.Reference.DefaultWarehouseId, fixture.Reference.CustomerId).WithRemark("by the supervisor").WithLine(_product.Id, 2m, 3m).Build();
        var saved = (await _s.Supervisor.SalesOrders.UpdateAsync(order.Id, request, order.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal("by the supervisor", saved.Remark);
        Assert.Equal(TestUsers.UsernameOf(Roles.Clerk), saved.CreatedBy); // the creator does not change
    }

    [Fact]
    public async Task A_save_with_the_star_or_without_the_header_is_428_and_changes_nothing()
    {
        var order = await Created();
        var request = Edited().WithLine(_product.Id, 1m, 1m).Build();

        (await _s.Admin.SendAsync(HttpMethod.Put, $"/api/sales-orders/{order.Id}", request, r => r.Headers.TryAddWithoutValidation("If-Match", "*"))).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        (await _s.Admin.SalesOrders.UpdateWithoutIfMatchAsync(order.Id, request)).ShouldBeProblem(HttpStatusCode.PreconditionRequired);

        Assert.Equal(order.ConcurrencyToken, (await ReadAsync(order)).ConcurrencyToken);
    }

    [Fact]
    public async Task A_stale_or_made_up_token_is_409_naming_the_order_and_changes_nothing()
    {
        var order = await Created();
        var saved = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited("first").WithLine(_product.Id, 1m, 1m).Build(), order.ConcurrencyToken)).Data<SalesOrderDto>();

        var stale = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited("second").WithLine(_product.Id, 9m, 9m).Build(), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited("third").WithLine(_product.Id, 9m, 9m).Build(), "not-a-token")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains(order.No, stale.Detail);
        var after = await ReadAsync(order);
        Assert.Equal(("first", saved.ConcurrencyToken), (after.Remark, after.ConcurrencyToken));
    }

    [Theory]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public async Task An_order_that_is_not_a_draft_cannot_be_edited_409(SalesOrderStatus status)
    {
        var order = await Created();
        await _s.SetStatusAsync(order, status);
        var current = await ReadAsync(order);

        var problem = (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 1m, 1m).Build(), current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNotEditable(order.No, status), problem.Detail);
        Assert.Equal(status, (await ReadAsync(order)).Status);
    }

    [Fact]
    public async Task The_warehouse_cannot_change_and_the_customer_must_still_be_an_active_customer()
    {
        var order = await Created();
        var elsewhere = await _s.NewWarehouseAsync();
        var supplier = await _s.NewPartnerAsync(PartnerType.Supplier);
        var inactive = await _s.NewPartnerAsync(isActive: false);
        SalesOrderSaveRequest Request(Guid warehouse, Guid customer) => new SalesOrderBuilder(warehouse, customer).WithLine(_product.Id, 1m, 1m).Build();

        Assert.Equal(Messages.WarehouseCannotChange, (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Request(elsewhere.Id, fixture.Reference.CustomerId), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerWrongType(DocumentType.Issue, PartnerType.Customer), (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Request(_warehouse.Id, supplier.Id), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerInactive, (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Request(_warehouse.Id, inactive.Id), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);

        var after = await ReadAsync(order);
        Assert.Equal((order.WarehouseId, order.CustomerId, order.ConcurrencyToken), (after.WarehouseId, after.CustomerId, after.ConcurrencyToken));
    }

    [Fact]
    public async Task A_refused_line_leaves_the_old_lines_in_place()
    {
        var order = await Created();
        var inactive = await _s.NewProductAsync(b => b.Inactive());

        (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 1m, 1m).WithLine(inactive.Id, 1m, 1m).Build(), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        var after = await ReadAsync(order);
        Assert.Equal((order.TotalAmount, order.ConcurrencyToken), (after.TotalAmount, after.ConcurrencyToken));
    }

    [Fact]
    public async Task Saving_writes_one_audit_event_with_the_old_and_the_new_summary()
    {
        var order = await Created();

        (await _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 2m, 5m).WithLine(_product.Id, 1m, 1m).Build(), order.ConcurrencyToken)).ShouldBeOk();

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderUpdated), ("@no", order.No)));
        var before = await fixture.Database.ScalarAsync<string>("SELECT [Before] FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderUpdated), ("@no", order.No));
        var after = await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.SalesOrderUpdated), ("@no", order.No));
        Assert.Equal((1, 25m), (Read(before, "Lines").GetInt32(), Read(before, "SellingAmount").GetDecimal()));
        Assert.Equal((2, 11m), (Read(after, "Lines").GetInt32(), Read(after, "SellingAmount").GetDecimal()));
    }

    private static System.Text.Json.JsonElement Read(string json, string key) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty(key);

    [Fact]
    public async Task Five_saves_of_the_same_version_at_the_same_time_give_one_200_and_four_409_and_one_consistent_order()
    {
        var order = await Created();

        var responses = await Concurrently.RunAsync(Enumerable.Range(1, 5).Select(i => (Func<Task<ApiResponse>>)(() =>
            _s.Admin.SalesOrders.UpdateAsync(order.Id, Edited($"writer {i}").WithLine(_product.Id, i, 1m).Build(), order.ConcurrencyToken))));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        var winner = responses.Single(r => r.Status == HttpStatusCode.OK).Data<SalesOrderDto>();
        var after = await ReadAsync(order);
        Assert.Equal((winner.Remark, winner.TotalAmount, winner.ConcurrencyToken), (after.Remark, after.TotalAmount, after.ConcurrencyToken));
        Assert.Single(after.Lines);
    }
}
