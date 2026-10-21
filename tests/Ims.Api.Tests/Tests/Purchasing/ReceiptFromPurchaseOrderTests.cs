using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.4 acceptance 1 to 3: <c>POST /api/purchase-orders/{id}/receipt</c> makes the Draft receipt of an Approved order at the order's prices, whoever asks (Warehouse staff never see the price, but the
/// stock is valued at it), and refuses every order that cannot be received.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReceiptFromPurchaseOrderTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    [Fact]
    public async Task A_receipt_from_an_approved_order_has_the_orders_warehouse_and_supplier_and_one_line_per_order_line_at_the_order_price()
    {
        var (a, b) = (await _p.NewProductAsync(), await _p.NewProductAsync());
        var order = await _p.ApprovedAsync((a.Id, 10m, 2.50m, null), (b.Id, 5m, 4m, null));

        var response = await _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id);
        var receipt = response.Data<DocumentDto>();

        Assert.Equal((DocumentType.Receipt, DocumentStatus.Draft, order.WarehouseId, order.SupplierId), (receipt.Type, receipt.Status, receipt.WarehouseId, receipt.PartnerId));
        Assert.Equal(new LinkedPurchaseOrder(order.Id, order.No), receipt.PurchaseOrder);
        Assert.StartsWith("RCV-", receipt.No); // the normal sequence
        Assert.Equal([1, 2], receipt.Lines.Select(l => l.PurchaseOrderLineNo));
        Assert.Equal([a.Id, b.Id], receipt.Lines.Select(l => l.ProductId));
        Assert.Equal([(10m, 2.50m, 25m), (5m, 4m, 20m)], receipt.Lines.Select(l => (l.Qty, l.UnitCost!.Value, l.Amount!.Value)));
        Assert.All(receipt.Lines, l => Assert.False(l.CostFromAverage)); // the price is the order's, never the average
        Assert.Equal(45m, receipt.TotalAmount);
        Assert.False(string.IsNullOrEmpty(response.Header("ETag")));
        Assert.Equal(receipt.ConcurrencyToken, response.Header("ETag")!.Trim('"'));
    }

    [Fact]
    public async Task The_order_unit_travels_with_the_line_and_the_price_is_per_ordered_unit()
    {
        var carton = (await _p.Admin.Units.CreateAsync(Unique.Code("CT"), "Carton")).Data<Application.Features.Catalog.UomDto>();
        var product = await _p.NewProductAsync(customize: b => b.WithConversion(carton.Id, 12m));
        var order = await _p.ApprovedAsync((product.Id, 3m, 120m, carton.Id));

        var receipt = await _p.ReceiptFromAsync(order);

        var line = Assert.Single(receipt.Lines);
        Assert.Equal((carton.Id, 3m, 36m, 120m, 360m), (line.UomId, line.Qty, line.BaseQty, line.UnitCost, line.Amount));
        Assert.Equal(10m, line.BaseUnitCost); // 120.00 per carton of 12 is 10.0000 per piece
    }

    [Fact]
    public async Task Warehouse_staff_get_the_receipt_without_a_price_but_the_stock_is_valued_at_the_order_price_after_posting()
    {
        var product = await _p.NewProductAsync();
        var order = await _p.ApprovedAsync((product.Id, 10m, 3.25m, null));

        var response = await _p.Staff.PurchaseOrders.CreateReceiptAsync(order.Id);
        var receipt = response.Data<DocumentDto>();

        var line = Assert.Single(receipt.Lines);
        Assert.Equal((10m, null, null, null), (line.Qty, line.UnitCost, line.BaseUnitCost, line.Amount)); // quantities yes, money no
        Assert.Null(receipt.TotalAmount);
        Assert.DoesNotContain("3.25", response.Body);

        var posted = await _p.PostAsync(receipt, _p.Staff);

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        var ledger = Assert.Single(await _p.Stock.LedgerAsync(product.Id, order.WarehouseId));
        Assert.Equal((10m, 3.25m), (ledger.QtyDelta, ledger.UnitCost));
        var balance = await _p.Stock.BalanceAsync(product.Id, order.WarehouseId);
        Assert.Equal(3.25m, balance!.AvgCost);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public async Task An_order_that_is_not_approved_cannot_be_received_and_the_answer_names_its_state(PurchaseOrderStatus status)
    {
        var order = await _p.CreateAsync(_p.Warehouse, (await _p.NewProductAsync()).Id, _p.Clerk);
        await _p.SetStatusAsync(order, status);

        var problem = (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains($"is {status}", problem.Detail);
        Assert.Contains(order.No, problem.Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE PurchaseOrderId = (SELECT Id FROM PurchaseOrder WHERE PublicId = @id)", ("@id", order.Id)));
    }

    [Fact]
    public async Task An_order_with_nothing_left_to_receive_is_refused_and_a_partly_received_one_is_prefilled_with_what_remains()
    {
        var (a, b) = (await _p.NewProductAsync(), await _p.NewProductAsync());
        var order = await _p.ApprovedAsync((a.Id, 10m, 2m, null), (b.Id, 5m, 3m, null));
        var first = await _p.ReceiptFromAsync(order);
        // receive all of line 1 and 2 of 5 of line 2
        (await _p.Clerk.Documents.UpdateAsync(first.Id, PurchasingScenario.Save(first, PurchasingScenario.LineFor(a.Id, 10m, 1), PurchasingScenario.LineFor(b.Id, 2m, 2)), first.ConcurrencyToken)).ShouldBeOk();
        await _p.PostAsync(await _p.Stock.GetAsync(first.Id), _p.Clerk);

        var second = await _p.ReceiptFromAsync(order);

        var line = Assert.Single(second.Lines); // line 1 has nothing left and is not copied
        Assert.Equal((b.Id, 3m, 2), (line.ProductId, line.Qty, line.PurchaseOrderLineNo));
        Assert.NotEqual(first.Id, second.Id);

        // receive the rest: nothing is left
        await _p.PostAsync(second, _p.Clerk);
        var problem = (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Contains("nothing left to receive", problem.Detail);
    }

    [Fact]
    public async Task While_the_module_is_off_the_endpoint_is_a_404_for_every_role()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 1m, 1m, null));
        await _p.TurnOffAsync();

        foreach (var client in new[] { _p.Admin, _p.Clerk, _p.Staff, _p.Supervisor })
            (await client.PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_receipt_needs_document_create_and_po_read_and_an_order_the_caller_can_see()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 1m, 1m, null));
        var other = await _p.NewWarehouseAsync();
        var elsewhere = (await _p.Admin.PurchaseOrders.CreateAsync(_p.Request(Guid.Parse(other.Id.ToString()), (await _p.NewProductAsync()).Id).Build())).Data<PurchaseOrderDto>();
        elsewhere = (await _p.Admin.PurchaseOrders.SubmitAsync(elsewhere.Id, elsewhere.ConcurrencyToken)).Data<PurchaseOrderDto>();

        (await _p.Owner.PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBe(HttpStatusCode.Forbidden); // no document.create
        (await Forged([Permissions.DocumentCreate]).PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBe(HttpStatusCode.Forbidden); // no po.read
        (await Forged([Permissions.DocumentCreate, Permissions.PoRead]).PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBeOk();
        (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(Guid.NewGuid())).ShouldBe(HttpStatusCode.NotFound);
        (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(elsewhere.Id)).ShouldBe(HttpStatusCode.NotFound); // another warehouse than the Clerk's: it does not exist for them
        (await _p.Admin.PurchaseOrders.CreateReceiptAsync(elsewhere.Id)).ShouldBeOk();
        (await fixture.Anonymous().PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_receipt_is_a_document_like_any_other_it_is_listed_and_an_audit_event_names_the_order()
    {
        var order = await _p.ApprovedAsync(((await _p.NewProductAsync()).Id, 4m, 1m, null));

        var receipt = await _p.ReceiptFromAsync(order, _p.Staff);

        var list = (await _p.Admin.Documents.ListAsync(type: DocumentType.Receipt, search: receipt.No)).Data<PagedResult<DocumentListItem>>();
        Assert.Contains(list.Items, i => i.Id == receipt.Id);
        Assert.Equal(TestUsers.UsernameOf(Roles.WarehouseStaff), receipt.CreatedBy); // the creator is the caller: Warehouse staff edit and submit their own documents
        Assert.Contains(receipt.AllowedActions, a => a == DocumentActions.Edit);
        var audit = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'document.created' AND EntityId = @no AND After LIKE @po", ("@no", receipt.No), ("@po", $"%{order.No}%"));
        Assert.Equal(1, audit);
    }

    [Fact]
    public async Task The_link_and_the_line_numbers_are_shown_only_to_a_caller_who_may_read_purchase_orders_and_only_they_can_change_a_linked_draft()
    {
        var product = await _p.NewProductAsync();
        var order = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        var receipt = await _p.ReceiptFromAsync(order);

        var blind = Forged([Permissions.StockRead, Permissions.DocumentCreate, Permissions.DocumentEditAny]); // reads and writes documents, not purchase orders
        var seen = (await blind.Documents.GetAsync(receipt.Id)).Data<DocumentDto>();
        Assert.Null(seen.PurchaseOrder);
        Assert.All(seen.Lines, l => Assert.Null(l.PurchaseOrderLineNo));
        var reader = Forged([Permissions.StockRead, Permissions.PoRead]);
        Assert.Equal(new LinkedPurchaseOrder(order.Id, order.No), (await reader.Documents.GetAsync(receipt.Id)).Data<DocumentDto>().PurchaseOrder);

        // a save by the caller who cannot read orders is refused before anything is looked at, so no figure of the order is quoted to them
        var save = PurchasingScenario.Save(receipt, PurchasingScenario.LineFor(product.Id, 99m, 1));
        var refused = (await blind.Documents.UpdateAsync(receipt.Id, save, receipt.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(Messages.ReceiptNeedsPoRead, refused.Detail);
        Assert.DoesNotContain("ordered", refused.Detail);
    }

    [Fact]
    public async Task A_document_that_is_not_linked_shows_no_order_and_a_linked_one_hides_it_while_the_module_is_off()
    {
        var product = await _p.NewProductAsync();
        var plain = await _p.Stock.DraftReceiptAsync(_p.Admin, _p.Warehouse, new Line(product.Id, 2m, 1m));
        var receipt = await _p.ReceiptFromAsync(await _p.ApprovedAsync((product.Id, 2m, 1m, null)));

        Assert.Null(plain.PurchaseOrder);
        Assert.All(plain.Lines, l => Assert.Null(l.PurchaseOrderLineNo));
        Assert.NotNull((await _p.Admin.Documents.GetAsync(receipt.Id)).Data<DocumentDto>().PurchaseOrder);

        await _p.TurnOffAsync();

        var hidden = (await _p.Admin.Documents.GetAsync(receipt.Id)).Data<DocumentDto>();
        Assert.Null(hidden.PurchaseOrder);
        Assert.All(hidden.Lines, l => Assert.Null(l.PurchaseOrderLineNo));
    }
}
