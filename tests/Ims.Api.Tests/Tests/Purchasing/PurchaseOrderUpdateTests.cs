using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.2 acceptance 4: a draft is edited by any holder of po.manage with the token of the version they read (If-Match). The lines are replaced, the number and the status stay, a missing
/// header is 428, a stale token is 409 and changes nothing, an order that is no longer a draft is 409, and two saves of the same version cannot both win.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderUpdateTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private Ims.Application.Features.Warehouses.WarehouseDto _warehouse = null!;
    private Ims.Application.Features.Catalog.ProductDto _product = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        _warehouse = await _p.NewWarehouseAsync();
        _product = await _p.NewProductAsync();
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private PurchaseOrderBuilder Edited(string remark = "edited") => new PurchaseOrderBuilder(_warehouse.Id, fixture.Reference.SupplierId).WithRemark(remark);

    private Task<PurchaseOrderDto> Created() => _p.CreateAsync(_warehouse.Id, _product.Id);

    private static System.Text.Json.JsonElement Read(string json, string key) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty(key);

    private async Task<PurchaseOrderDto> ReadAsync(PurchaseOrderDto order) => (await _p.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>();

    [Fact]
    public async Task A_save_replaces_the_header_and_all_lines_and_keeps_the_number_and_the_status()
    {
        var order = await Created();
        var other = await _p.NewProductAsync();
        var request = Edited().WithLine(other.Id, 4m, 12.5m).WithLine(_product.Id, 1m, 0.5m).Build();

        var response = await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, request, order.ConcurrencyToken);
        var saved = response.Data<PurchaseOrderDto>();

        Assert.Equal((order.No, PurchaseOrderStatus.Draft, "edited"), (saved.No, saved.Status, saved.Remark));
        Assert.Equal([other.Id, _product.Id], saved.Lines.Select(l => l.ProductId));
        Assert.Equal([1, 2], saved.Lines.Select(l => l.LineNo));
        Assert.Equal(50.5m, saved.TotalAmount);
        Assert.NotEqual(order.ConcurrencyToken, saved.ConcurrencyToken);
        Assert.Equal($"\"{saved.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.Equal(saved.Lines.Select(l => l.Amount), (await ReadAsync(order)).Lines.Select(l => l.Amount));
        Assert.Equal(2, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrderLine l JOIN PurchaseOrder o ON o.Id = l.PurchaseOrderId WHERE o.PublicId = @id AND l.REC_DELETED = 0", ("@id", order.Id)));
    }

    [Fact]
    public async Task Any_holder_of_po_manage_may_edit_any_draft_not_only_their_own()
    {
        var defaultWarehouse = fixture.Reference.DefaultWarehouseId;
        var order = await _p.CreateAsync(defaultWarehouse, _product.Id, by: _p.Clerk);

        var request = new PurchaseOrderBuilder(defaultWarehouse, fixture.Reference.SupplierId).WithRemark("by the supervisor").WithLine(_product.Id, 2m, 3m).Build();
        var saved = (await _p.Supervisor.PurchaseOrders.UpdateAsync(order.Id, request, order.ConcurrencyToken)).Data<PurchaseOrderDto>();

        Assert.Equal("by the supervisor", saved.Remark);
        Assert.Equal(TestUsers.UsernameOf(Roles.Clerk), saved.CreatedBy); // the creator does not change
    }

    [Fact]
    public async Task A_save_without_the_header_is_428_and_changes_nothing()
    {
        var order = await Created();

        (await _p.Admin.PurchaseOrders.UpdateWithoutIfMatchAsync(order.Id, Edited().WithLine(_product.Id, 1m, 1m).Build())).ShouldBeProblem(HttpStatusCode.PreconditionRequired);

        Assert.Equal(order.ConcurrencyToken, (await ReadAsync(order)).ConcurrencyToken);
    }

    [Fact]
    public async Task A_stale_or_made_up_token_is_409_naming_the_order_and_changes_nothing()
    {
        var order = await Created();
        var saved = (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited("first").WithLine(_product.Id, 1m, 1m).Build(), order.ConcurrencyToken)).Data<PurchaseOrderDto>();

        var stale = (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited("second").WithLine(_product.Id, 9m, 9m).Build(), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited("third").WithLine(_product.Id, 9m, 9m).Build(), "not-a-token")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains(order.No, stale.Detail);
        var after = await ReadAsync(order);
        Assert.Equal(("first", saved.ConcurrencyToken), (after.Remark, after.ConcurrencyToken));
    }

    [Fact]
    public async Task The_token_of_another_order_is_refused()
    {
        var a = await Created();
        var b = await Created();

        (await _p.Admin.PurchaseOrders.UpdateAsync(a.Id, Edited().WithLine(_product.Id, 1m, 1m).Build(), b.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task An_order_that_is_not_a_draft_cannot_be_edited_409(PurchaseOrderStatus status)
    {
        var order = await Created();
        await _p.SetStatusAsync(order, status);
        var current = await ReadAsync(order);

        var problem = (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 1m, 1m).Build(), current.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.PurchaseOrderNotEditable(order.No, status), problem.Detail);
        Assert.Equal(status, (await ReadAsync(order)).Status);
    }

    [Fact]
    public async Task The_warehouse_cannot_change_and_the_supplier_must_still_be_an_active_supplier()
    {
        var order = await Created();
        var elsewhere = await _p.NewWarehouseAsync();
        var customer = await _p.NewPartnerAsync(PartnerType.Customer);
        var inactive = await _p.NewPartnerAsync(isActive: false);
        PurchaseOrderSaveRequest Request(Guid warehouse, Guid supplier) => new PurchaseOrderBuilder(warehouse, supplier).WithLine(_product.Id, 1m, 1m).Build();

        Assert.Equal(Messages.WarehouseCannotChange, (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Request(elsewhere.Id, fixture.Reference.SupplierId), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.SupplierRequired, (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Request(_warehouse.Id, customer.Id), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(Messages.PartnerInactive, (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Request(_warehouse.Id, inactive.Id), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);

        var after = await ReadAsync(order);
        Assert.Equal((order.WarehouseId, order.SupplierId, order.ConcurrencyToken), (after.WarehouseId, after.SupplierId, after.ConcurrencyToken)); // every refusal left the draft as it was
    }

    [Fact]
    public async Task A_refused_line_leaves_the_old_lines_in_place()
    {
        var order = await Created();
        var inactive = await _p.NewProductAsync(b => b.Inactive());

        (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 1m, 1m).WithLine(inactive.Id, 1m, 1m).Build(), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        var after = await ReadAsync(order);
        Assert.Equal((order.TotalAmount, order.ConcurrencyToken), (after.TotalAmount, after.ConcurrencyToken));
    }

    [Fact]
    public async Task An_order_that_does_not_exist_is_404()
    {
        (await _p.Admin.PurchaseOrders.UpdateAsync(Guid.NewGuid(), Edited().WithLine(_product.Id, 1m, 1m).Build())).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saving_validates_the_body_like_creating_does()
    {
        var order = await Created();

        var problem = (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 0m, 1.005m).Build(), order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor("lines[0].qty"));
        Assert.NotEmpty(problem.ErrorsFor("lines[0].unitPrice"));
    }

    [Fact]
    public async Task Saving_writes_one_audit_event_with_the_old_and_the_new_summary()
    {
        var order = await Created();

        (await _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited().WithLine(_product.Id, 2m, 5m).WithLine(_product.Id, 1m, 1m).Build(), order.ConcurrencyToken)).ShouldBeOk();

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.PurchaseOrderUpdated), ("@no", order.No)));
        var before = await fixture.Database.ScalarAsync<string>("SELECT [Before] FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.PurchaseOrderUpdated), ("@no", order.No));
        var after = await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.PurchaseOrderUpdated), ("@no", order.No));
        Assert.Equal((1, 25m), (Read(before, "Lines").GetInt32(), Read(before, "Amount").GetDecimal()));
        Assert.Equal((2, 11m), (Read(after, "Lines").GetInt32(), Read(after, "Amount").GetDecimal()));
    }

    [Fact]
    public async Task Five_saves_of_the_same_version_at_the_same_time_give_one_200_and_four_409_and_one_consistent_order()
    {
        var order = await Created();

        var responses = await Concurrently.RunAsync(Enumerable.Range(1, 5).Select(i => (Func<Task<ApiResponse>>)(() =>
            _p.Admin.PurchaseOrders.UpdateAsync(order.Id, Edited($"writer {i}").WithLine(_product.Id, i, 1m).Build(), order.ConcurrencyToken))));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
        var winner = responses.Single(r => r.Status == HttpStatusCode.OK).Data<PurchaseOrderDto>();
        var after = await ReadAsync(order);
        Assert.Equal((winner.Remark, winner.TotalAmount, winner.ConcurrencyToken), (after.Remark, after.TotalAmount, after.ConcurrencyToken));
        Assert.Single(after.Lines);
    }
}
