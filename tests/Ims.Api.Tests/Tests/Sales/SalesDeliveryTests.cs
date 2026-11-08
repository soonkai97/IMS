using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.5 acceptance 1 to 4 on a real SQL Server: a delivery is created from an Approved sales order (an Issue with purpose Sale, the order's warehouse and customer, one line per order line with
/// something left, no price), its lines name the order's lines and may not exceed what remains, its customer and warehouse cannot change, and the link can be neither set nor cleared by a request.
/// Posting a linked delivery is PL3.7: until then it is refused, so nothing leaves stock unchecked.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesDeliveryTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    /// <summary>An Approved order in the default warehouse for the default customer: line 1 is 10 of <paramref name="a"/> at 2.50, line 2 is 5 of <paramref name="b"/> at 4.00 (released at once: below every rule).</summary>
    private async Task<SalesOrderDto> ApprovedAsync(Guid a, Guid b)
    {
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(a, 10m, 2.50m).WithLine(b, 5m, 4m).Build())).Data<SalesOrderDto>();
        var approved = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        Assert.Equal(SalesOrderStatus.Approved, approved.Status);
        return approved;
    }

    private async Task<(SalesOrderDto Order, DocumentDto Delivery, Guid A, Guid B)> DeliveryAsync()
    {
        var a = (await _s.NewProductAsync()).Id;
        var b = (await _s.NewProductAsync()).Id;
        var order = await ApprovedAsync(a, b);
        return (order, (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>(), a, b);
    }

    private static DocumentSaveRequest Save(DocumentDto d, params DocumentLineInput[] lines) => new(d.WarehouseId, d.PartnerId, IssuePurpose.Sale, d.Remark, lines);

    private static DocumentLineInput Line(Guid product, decimal qty, int? orderLineNo, Guid? uom = null) => new(product, qty, uom, null, null, null, orderLineNo);

    // ----- acceptance 1 -----
    [Fact]
    public async Task A_delivery_from_an_approved_order_is_a_draft_issue_with_purpose_sale_one_line_per_order_line_in_the_orders_unit_and_no_price()
    {
        var (order, delivery, a, b) = await DeliveryAsync();

        Assert.Equal((DocumentType.Issue, DocumentStatus.Draft, IssuePurpose.Sale), (delivery.Type, delivery.Status, delivery.Purpose));
        Assert.Equal((order.WarehouseId, order.CustomerId), (delivery.WarehouseId, delivery.PartnerId));
        Assert.Equal(new LinkedSalesOrder(order.Id, order.No), delivery.SalesOrder);
        Assert.Equal([(a, 10m, 1), (b, 5m, 2)], delivery.Lines.Select(l => (l.ProductId, l.Qty, l.SalesOrderLineNo!.Value)));
        Assert.All(delivery.Lines, l => Assert.Equal(order.Lines.Single(o => o.ProductId == l.ProductId).UomId, l.UomId));
        Assert.Null(delivery.PurchaseOrder);
        Assert.StartsWith($"ISS-", delivery.No); // the normal sequence of an issue
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityId = @no", ("@a", AuditActions.DocumentCreated), ("@no", delivery.No)));
    }

    [Fact]
    public async Task The_delivery_carries_no_price_anywhere_not_the_orders_selling_price_and_not_a_cost_for_a_caller_who_may_see_costs()
    {
        var a = (await _s.NewProductAsync()).Id;
        var order = await ApprovedAsync(a, (await _s.NewProductAsync()).Id);

        var forStaff = await _s.Staff.SalesOrders.CreateDeliveryAsync(order.Id);
        var forAdmin = await _s.Admin.SalesOrders.CreateDeliveryAsync(order.Id);

        foreach (var response in new[] { forStaff, forAdmin })
        {
            response.ShouldBeOk();
            Assert.DoesNotContain("2.5", response.Body);                 // the order's selling price
            Assert.DoesNotContain("sellingPrice", response.Body, StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(forAdmin.Data<DocumentDto>().Lines, l => Assert.Null(l.UnitCost));    // nobody typed a cost: the line is valued at the average when it posts
        Assert.All(forAdmin.Data<DocumentDto>().Lines, l => Assert.True(l.CostFromAverage));
    }

    // ----- acceptance 2 -----
    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public async Task An_order_that_is_not_approved_cannot_be_delivered_409_naming_the_status(SalesOrderStatus status)
    {
        var order = await ApprovedAsync((await _s.NewProductAsync()).Id, (await _s.NewProductAsync()).Id);
        await _s.SetStatusAsync(order, status);
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId IS NOT NULL");

        var problem = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, status), problem.Detail);
        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId IS NOT NULL"));
    }

    [Fact]
    public async Task An_order_with_nothing_left_to_deliver_is_409_and_a_line_with_nothing_left_is_not_copied()
    {
        var (order, first, a, b) = await DeliveryAsync();
        // What Posted deliveries took out is what reduces the remaining quantity; posting itself is PL3.7, so the two rows are set the way a post leaves them.
        await fixture.Database.ExecuteAsync("DELETE FROM StockDocumentLine WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no) AND SalesOrderLineNo = 2", ("@no", first.No)); // the first delivery took line 1 only
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = 3 WHERE No = @no", ("@no", first.No));

        var second = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();

        Assert.Equal([(b, 5m, 2)], second.Lines.Select(l => (l.ProductId, l.Qty, l.SalesOrderLineNo!.Value))); // line 1 is fully delivered
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = 3 WHERE No = @no", ("@no", second.No));
        var none = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.SalesOrderNothingToDeliver(order.No), none.Detail);
    }

    [Fact]
    public async Task Creating_a_delivery_needs_document_create_and_so_read_and_a_caller_outside_the_orders_warehouse_finds_no_order()
    {
        var order = await ApprovedAsync((await _s.NewProductAsync()).Id, (await _s.NewProductAsync()).Id);
        var before = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId IS NOT NULL");

        (await Forged([Permissions.DocumentCreate]).SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);               // no so.read
        (await Forged([Permissions.SoRead]).SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);                       // no document.create
        var other = await _s.NewWarehouseAsync();
        var (scoped, _) = await new AccountFactory(fixture).SignInAsync(await new AccountFactory(fixture).CreateAsync(Roles.Clerk, [other.Id]));
        (await scoped.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal(before, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId IS NOT NULL"));
    }

    [Fact]
    public async Task While_the_sales_module_is_off_creating_a_delivery_is_404_for_every_signed_in_caller_and_401_without_a_token()
    {
        var order = await ApprovedAsync((await _s.NewProductAsync()).Id, (await _s.NewProductAsync()).Id);
        await _s.TurnOffAsync();

        foreach (var who in new[] { _s.Admin, _s.Clerk, _s.Staff, _s.Owner }) (await who.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal(401, (await fixture.Anonymous().SalesOrders.CreateDeliveryAsync(order.Id)).StatusCode);
    }

    [Fact]
    public async Task Two_deliveries_may_be_open_for_one_order_at_once_a_draft_reserves_nothing()
    {
        var (order, first, _, _) = await DeliveryAsync();

        var second = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();

        Assert.NotEqual(first.No, second.No);
        Assert.Equal(2, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE SalesOrderId = (SELECT Id FROM SalesOrder WHERE PublicId = @id)", ("@id", order.Id)));
    }

    // ----- acceptance 3 -----
    [Fact]
    public async Task A_linked_draft_saves_lines_that_name_order_lines_within_what_remains_and_may_drop_or_reduce_lines()
    {
        var (_, delivery, a, b) = await DeliveryAsync();

        var saved = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 4m, 1), Line(a, 6m, 1)), delivery.ConcurrencyToken)).Data<DocumentDto>(); // 4 + 6 = 10: exactly what remains
        var reduced = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(saved, Line(b, 2m, 2)), saved.ConcurrencyToken)).Data<DocumentDto>();

        Assert.Equal([(a, 4m, 1), (a, 6m, 1)], saved.Lines.Select(l => (l.ProductId, l.Qty, l.SalesOrderLineNo!.Value)));
        Assert.Equal([(b, 2m, 2)], reduced.Lines.Select(l => (l.ProductId, l.Qty, l.SalesOrderLineNo!.Value)));
        Assert.Equal(delivery.SalesOrder, reduced.SalesOrder); // the link is intact
    }

    [Fact]
    public async Task A_save_over_what_remains_is_400_naming_the_line_and_the_remaining_quantity_and_changes_nothing()
    {
        var (_, delivery, a, _) = await DeliveryAsync();

        var problem = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 7m, 1), Line(a, 4m, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Remaining 10", problem.ErrorsFor("lines[0].qty").Single()); // 7 + 4 = 11 against 10: the lines are added up first
        var after = (await _s.Clerk.Documents.GetAsync(delivery.Id)).Data<DocumentDto>();
        Assert.Equal((delivery.ConcurrencyToken, delivery.Lines.Count), (after.ConcurrencyToken, after.Lines.Count));
    }

    [Fact]
    public async Task A_save_without_the_order_line_number_is_400_and_the_link_stays_intact()
    {
        var (_, delivery, a, _) = await DeliveryAsync();

        var problem = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, null)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.DeliveryLineNeedsOrderLine, problem.ErrorsFor("lines[0].salesOrderLineNo").Single());
        Assert.Equal(delivery.SalesOrder, (await _s.Clerk.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().SalesOrder);
    }

    [Fact]
    public async Task A_line_that_names_another_orders_line_another_product_or_another_unit_is_400_with_its_own_sentence()
    {
        var (order, delivery, a, b) = await DeliveryAsync();
        var carton = fixture.Reference.CartonUnitId;

        var unknown = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, 9)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var wrongProduct = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(b, 1m, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var wrongUnit = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, 1, carton)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.DeliveryLineUnknownOrderLine(9, order.No), unknown.ErrorsFor("lines[0].salesOrderLineNo").Single());
        Assert.Contains("is for", wrongProduct.ErrorsFor("lines[0].salesOrderLineNo").Single());
        Assert.Contains("deliver it in", wrongUnit.ErrorsFor("lines[0].salesOrderLineNo").Single());
    }

    [Fact]
    public async Task The_customer_and_the_warehouse_of_a_delivery_cannot_change()
    {
        var (_, delivery, a, _) = await DeliveryAsync();
        var otherCustomer = await _s.NewPartnerAsync();
        var elsewhere = await _s.NewWarehouseAsync();

        var customer = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, new DocumentSaveRequest(delivery.WarehouseId, otherCustomer.Id, IssuePurpose.Sale, null, [Line(a, 1m, 1)]), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var warehouse = (await _s.Admin.Documents.UpdateAsync(delivery.Id, new DocumentSaveRequest(elsewhere.Id, delivery.PartnerId, IssuePurpose.Sale, null, [Line(a, 1m, 1)]), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var purpose = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, new DocumentSaveRequest(delivery.WarehouseId, delivery.PartnerId, IssuePurpose.Use, null, [Line(a, 1m, 1)]), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.DeliveryCustomerLocked, customer.ErrorsFor("partnerId").Single());
        Assert.Equal(Messages.WarehouseCannotChange, warehouse.Detail);
        Assert.Equal(Messages.DeliveryHeaderLocked, purpose.ErrorsFor("purpose").Single());
    }

    [Fact]
    public async Task A_request_cannot_set_or_clear_the_link_extra_properties_are_ignored()
    {
        var (order, delivery, a, _) = await DeliveryAsync();
        var plain = await _s.Stock.DraftIssueAsync(_s.Clerk, _s.Warehouse, new Line(a, 1m, 1m));
        var other = await ApprovedAsync(a, (await _s.NewProductAsync()).Id);
        string Body(Guid warehouse, Guid? partner, int? lineNo, string extra) =>
            $$"""{"warehouseId":"{{warehouse}}","partnerId":"{{partner}}","purpose":"Sale","lines":[{"productId":"{{a}}","qty":1{{(lineNo is null ? "" : $",\"salesOrderLineNo\":{lineNo}")}}}],{{extra}}}""";

        // a body that names an order: on the delivery it changes nothing, on a plain issue it does not link it
        (await _s.Clerk.PutRawJsonAsync($"/api/documents/{delivery.Id}", Body(delivery.WarehouseId, delivery.PartnerId, 1, $"\"salesOrderId\":\"{other.Id}\",\"salesOrder\":null"), delivery.ConcurrencyToken)).ShouldBeOk();
        var after = (await _s.Clerk.Documents.GetAsync(delivery.Id)).Data<DocumentDto>();
        Assert.Equal(new LinkedSalesOrder(order.Id, order.No), after.SalesOrder);
        (await _s.Clerk.PutRawJsonAsync($"/api/documents/{plain.Id}", Body(plain.WarehouseId, plain.PartnerId, null, $"\"salesOrderId\":\"{other.Id}\""), plain.ConcurrencyToken)).ShouldBeOk();
        Assert.Null((await _s.Clerk.Documents.GetAsync(plain.Id)).Data<DocumentDto>().SalesOrder);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument WHERE No = @no AND SalesOrderId IS NOT NULL", ("@no", plain.No)));
    }

    [Fact]
    public async Task Naming_an_order_line_on_an_unlinked_document_is_400_not_linked_and_with_the_module_off_400_not_available()
    {
        var a = (await _s.NewProductAsync()).Id;
        var plain = await _s.Stock.DraftIssueAsync(_s.Clerk, _s.Warehouse, new Line(a, 1m, 1m));

        var on = (await _s.Clerk.Documents.UpdateAsync(plain.Id, Save(plain, Line(a, 1m, 1)), plain.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        await _s.TurnOffAsync();
        var off = (await _s.Clerk.Documents.UpdateAsync(plain.Id, Save(plain, Line(a, 1m, 1)), plain.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.DeliveryNotLinked, on.ErrorsFor("lines[0].salesOrderLineNo").Single());
        Assert.Equal(Messages.ModuleFieldNotAvailable("lines[0].salesOrderLineNo", ModuleCodes.Sales), off.ErrorsFor("lines[0].salesOrderLineNo").Single());
    }

    [Fact]
    public async Task A_receipt_from_a_purchase_order_and_a_delivery_do_not_take_each_others_line_numbers()
    {
        var (_, delivery, a, _) = await DeliveryAsync();

        var problem = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, new DocumentLineInput(a, 1m, null, null, null, 1, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.DeliveryNotLinked, problem.ErrorsFor("lines[0].purchaseOrderLineNo").Single());
    }

    // ----- the link and the module / permission, once the order is not open any more -----
    [Fact]
    public async Task A_linked_draft_is_not_saved_once_the_order_is_cancelled_or_without_so_read_or_with_the_module_off()
    {
        var (order, delivery, a, _) = await DeliveryAsync();
        var noRead = Forged([Permissions.DocumentCreate, Permissions.StockRead, Permissions.MasterDataRead, Permissions.DocumentEditAny]);

        (await noRead.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        await _s.SetStatusAsync(order, SalesOrderStatus.Cancelled);
        var cancelled = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        await _s.SetStatusAsync(order, SalesOrderStatus.Approved);
        await _s.TurnOffAsync();
        var off = (await _s.Clerk.Documents.UpdateAsync(delivery.Id, Save(delivery, Line(a, 1m, 1)), delivery.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, SalesOrderStatus.Cancelled), cancelled.Detail);
        Assert.Equal(Messages.DeliveryModuleOff, off.Detail);
    }

    [Fact]
    public async Task The_link_and_the_order_line_numbers_are_hidden_from_a_caller_without_so_read_and_while_the_module_is_off()
    {
        var (_, delivery, _, _) = await DeliveryAsync();

        var blind = (await Forged([Permissions.DocumentCreate, Permissions.StockRead, Permissions.MasterDataRead]).Documents.GetAsync(delivery.Id)).Data<DocumentDto>();
        await _s.TurnOffAsync();
        var off = (await _s.Clerk.Documents.GetAsync(delivery.Id)).Data<DocumentDto>();

        foreach (var read in new[] { blind, off })
        {
            Assert.Null(read.SalesOrder);
            Assert.All(read.Lines, l => Assert.Null(l.SalesOrderLineNo));
        }
        Assert.DoesNotContain(DocumentActions.Edit, off.AllowedActions); // and what the API would refuse is not offered
    }

    // ----- submit and post -----
    [Fact]
    public async Task Submitting_a_delivery_checks_the_quantities_again_and_posting_a_linked_delivery_is_refused_until_PL3_7()
    {
        var (_, delivery, a, _) = await DeliveryAsync();
        await fixture.Database.ExecuteAsync("UPDATE StockDocumentLine SET Qty = 11, BaseQty = 11 WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no) AND SalesOrderLineNo = 1", ("@no", delivery.No)); // a stored draft that is over
        var over = (await _s.Clerk.Documents.SubmitAsync(delivery.Id, (await _s.Clerk.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("is more than remains", over.Detail);

        // An approved linked delivery: set the way approval leaves it, since stock is not the point here.
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = 2 WHERE No = @no", ("@no", delivery.No));
        var refused = (await _s.Admin.Documents.PostAsync(delivery.Id, (await _s.Admin.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.DeliveryCannotPostYet, refused.Detail);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockLedgerEntry WHERE SourceDocId = (SELECT Id FROM StockDocument WHERE No = @no)", ("@no", delivery.No)));
        Assert.Equal(DocumentStatus.Approved, (await _s.Admin.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().Status);
    }
}
