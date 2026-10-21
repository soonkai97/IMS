using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.4 acceptance 4: the save rules of a receipt created from an order. Every line names an order line of the same order, with that line's product and unit, within what remains (Posted receipts
/// only reduce it); the supplier and warehouse are the order's; lines may be removed or reduced; the link is set at creation and nothing in a request can change it.
/// </summary>
[Collection(ApiCollection.Name)]
public class LinkedReceiptSaveTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private ProductDto _a = null!;
    private ProductDto _b = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        (_a, _b) = (await _p.NewProductAsync(), await _p.NewProductAsync());
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    /// <summary>An order of 10 of A (line 1) and 5 of B (line 2) with its prefilled receipt, made by the Clerk.</summary>
    private async Task<(PurchaseOrderDto Order, DocumentDto Receipt)> ReceiptAsync()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null), (_b.Id, 5m, 3m, null));
        return (order, await _p.ReceiptFromAsync(order));
    }

    /// <summary>Saves as <paramref name="by"/> with the CURRENT token of the draft, so one test can save it several times.</summary>
    private async Task<ApiResponse> SaveAsync(DocumentDto receipt, ApiClient by, params DocumentLineInput[] lines) =>
        await by.Documents.UpdateAsync(receipt.Id, Save(receipt, lines), (await _p.Stock.GetAsync(receipt.Id)).ConcurrencyToken);

    [Fact]
    public async Task Lines_may_be_reduced_or_removed_and_the_link_and_the_line_numbers_stay()
    {
        var (order, receipt) = await ReceiptAsync();

        var saved = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 4m, 1, 2m))).Data<DocumentDto>(); // line 2 removed, line 1 reduced

        Assert.Equal(new LinkedPurchaseOrder(order.Id, order.No), saved.PurchaseOrder);
        var line = Assert.Single(saved.Lines);
        Assert.Equal((_a.Id, 4m, 1, 8m), (line.ProductId, line.Qty, line.PurchaseOrderLineNo, line.Amount));
        Assert.Equal(order.SupplierId, saved.PartnerId);
    }

    [Fact]
    public async Task A_save_without_the_order_line_number_is_a_400_naming_the_line_and_the_draft_is_unchanged()
    {
        var (_, receipt) = await ReceiptAsync();

        var problem = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 4m, 1), LineFor(_b.Id, 1m, null))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains(Messages.ReceiptLineNeedsOrderLine, problem.ErrorsFor("lines[1].purchaseOrderLineNo"));
        var after = await _p.Stock.GetAsync(receipt.Id);
        Assert.Equal(receipt.Lines.Select(l => (l.ProductId, l.Qty, l.PurchaseOrderLineNo)), after.Lines.Select(l => (l.ProductId, l.Qty, l.PurchaseOrderLineNo)));
        Assert.Equal(receipt.PurchaseOrder, after.PurchaseOrder); // the link is intact
    }

    [Fact]
    public async Task Every_wrong_line_is_reported_unknown_number_another_product_another_unit()
    {
        var carton = (await _p.Admin.Units.CreateAsync(Unique.Code("CT"), "Carton")).Data<UomDto>();
        var c = await _p.NewProductAsync(customize: b => b.WithConversion(carton.Id, 12m));
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null), (c.Id, 2m, 100m, carton.Id));
        var receipt = await _p.ReceiptFromAsync(order);

        var problem = (await SaveAsync(receipt, _p.Clerk,
            LineFor(_a.Id, 1m, 7),                 // no order line 7
            LineFor(_b.Id, 1m, 1),                 // line 1 is for A
            LineFor(c.Id, 1m, 2),                  // line 2 is ordered in cartons
            LineFor(_a.Id, 1m, 1))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.ReceiptLineUnknownOrderLine(7, order.No), Assert.Single(problem.ErrorsFor("lines[0].purchaseOrderLineNo")));
        Assert.Equal(Messages.ReceiptLineWrongProduct(1, _a.Sku), Assert.Single(problem.ErrorsFor("lines[1].purchaseOrderLineNo")));
        Assert.Contains("ordered in", Assert.Single(problem.ErrorsFor("lines[2].purchaseOrderLineNo")));
        Assert.Empty(problem.ErrorsFor("lines[3].purchaseOrderLineNo"));
    }

    [Fact]
    public async Task A_quantity_above_what_remains_is_a_400_that_names_the_remaining_quantity_and_exactly_the_remainder_saves()
    {
        var (_, receipt) = await ReceiptAsync();

        var problem = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 10.5m, 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        var message = Assert.Single(problem.ErrorsFor("lines[0].qty"));
        Assert.Contains("Remaining 10", message);
        Assert.Contains("ordered 10", message);
        Assert.Contains(_a.Sku, message);

        (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 10m, 1))).ShouldBeOk();
    }

    [Fact]
    public async Task Lines_that_name_the_same_order_line_are_added_up_so_splitting_a_quantity_does_not_get_past_the_check()
    {
        var (_, receipt) = await ReceiptAsync();

        var problem = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 6m, 1), LineFor(_a.Id, 5m, 1))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Remaining 10", Assert.Single(problem.ErrorsFor("lines[0].qty")));
        (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 6m, 1), LineFor(_a.Id, 4m, 1))).ShouldBeOk();
    }

    [Fact]
    public async Task What_a_posted_receipt_took_is_no_longer_available_but_a_draft_or_a_submitted_receipt_takes_nothing()
    {
        var (order, receipt) = await ReceiptAsync();
        var other = await _p.ReceiptFromAsync(order);
        (await SaveAsync(other, _p.Clerk, LineFor(_a.Id, 6m, 1))).ShouldBeOk();
        var submitted = await _p.Stock.SubmitAsync(_p.Clerk, await _p.Stock.GetAsync(other.Id));
        Assert.Equal(DocumentStatus.Submitted, submitted.Status);

        // 6 sit on a Submitted receipt: they reserve nothing, the whole 10 is still open to this draft
        (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 10m, 1))).ShouldBeOk();

        await _p.Stock.CompleteAsync(submitted); // now 6 are posted
        var draft = await _p.Stock.GetAsync(receipt.Id);

        var problem = (await SaveAsync(draft, _p.Clerk, LineFor(_a.Id, 5m, 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        var message = Assert.Single(problem.ErrorsFor("lines[0].qty"));
        Assert.Contains("Remaining 4", message);
        Assert.Contains("received 6", message);
        (await SaveAsync(draft, _p.Clerk, LineFor(_a.Id, 4m, 1))).ShouldBeOk();
    }

    [Fact]
    public async Task The_same_product_on_two_order_lines_is_judged_per_line_never_per_product()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null), (_a.Id, 5m, 3m, null)); // line 1 and line 2 are both product A
        var receipt = await _p.ReceiptFromAsync(order);
        Assert.Equal([1, 2], receipt.Lines.Select(l => l.PurchaseOrderLineNo));
        Assert.Equal([2m, 3m], receipt.Lines.Select(l => l.UnitCost!.Value)); // each at the price of its own line

        (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 10m, 1), LineFor(_a.Id, 5m, 2))).ShouldBeOk();
        var over = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 10m, 1), LineFor(_a.Id, 6m, 2))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("Remaining 5", Assert.Single(over.ErrorsFor("lines[1].qty"))); // line 2 holds 5, though 15 of A are ordered

        // post line 1 in full, then line 1 has nothing left while line 2 is untouched
        var first = await _p.PostAsync((await _p.Admin.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(_a.Id, 10m, 1)), (await _p.Stock.GetAsync(receipt.Id)).ConcurrencyToken)).Data<DocumentDto>(), _p.Clerk);
        var again = await _p.ReceiptFromAsync(order);
        Assert.Equal([2], again.Lines.Select(l => l.PurchaseOrderLineNo)); // only line 2 is offered
        Assert.NotEqual(first.Id, again.Id);
        (await SaveAsync(again, _p.Clerk, LineFor(_a.Id, 1m, 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await SaveAsync(again, _p.Clerk, LineFor(_a.Id, 5m, 2))).ShouldBeOk();
    }

    [Fact]
    public async Task The_supplier_and_the_warehouse_cannot_be_changed()
    {
        var (order, receipt) = await ReceiptAsync();
        var otherSupplier = await _p.NewPartnerAsync();
        var otherWarehouse = await _p.NewWarehouseAsync();

        var supplier = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(_a.Id, 1m, 1)) with { PartnerId = otherSupplier.Id }, receipt.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var none = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(_a.Id, 1m, 1)) with { PartnerId = null }, receipt.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var warehouse = (await _p.Admin.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(_a.Id, 1m, 1)) with { WarehouseId = otherWarehouse.Id }, receipt.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.ReceiptSupplierLocked, supplier.Detail);
        Assert.Equal(Messages.ReceiptSupplierLocked, none.Detail);
        Assert.Equal(Messages.WarehouseCannotChange, warehouse.Detail);
        Assert.Equal(order.SupplierId, (await _p.Stock.GetAsync(receipt.Id)).PartnerId);
    }

    [Fact]
    public async Task The_link_is_set_at_creation_and_nothing_in_a_request_changes_it()
    {
        var (order, receipt) = await ReceiptAsync();
        var another = await _p.ApprovedAsync((_a.Id, 10m, 9m, null));

        // a body that also names another order (the contract has no such field: it is ignored) and a PUT of a plain header
        var body = new
        {
            warehouseId = receipt.WarehouseId, partnerId = receipt.PartnerId, remark = "x", purchaseOrderId = another.Id, purchaseOrder = new { id = another.Id, no = another.No },
            lines = new[] { new { productId = _a.Id, qty = 2m, unitCost = (decimal?)null, purchaseOrderLineNo = (int?)1, purchaseOrderId = another.Id } }
        };
        (await _p.Clerk.SendAsync(HttpMethod.Put, $"/api/documents/{receipt.Id}", body, r => r.Headers.TryAddWithoutValidation("If-Match", $"\"{receipt.ConcurrencyToken}\""))).ShouldBeOk();

        var after = await _p.Stock.GetAsync(receipt.Id);
        Assert.Equal(new LinkedPurchaseOrder(order.Id, order.No), after.PurchaseOrder);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM StockDocument d JOIN PurchaseOrder o ON o.Id = d.PurchaseOrderId WHERE d.PublicId = @d AND o.PublicId = @o", ("@d", receipt.Id), ("@o", order.Id)));
    }

    [Fact]
    public async Task A_line_naming_an_order_line_on_a_document_that_is_not_linked_is_a_400_and_while_the_module_is_off_it_is_not_available()
    {
        var plain = await _p.Stock.DraftReceiptAsync(_p.Admin, _p.Warehouse, new Line(_a.Id, 2m, 1m));
        var withLine = new DocumentSaveRequest(plain.WarehouseId, plain.PartnerId, null, null, [LineFor(_a.Id, 2m, 1, 1m)]);

        var onUpdate = (await _p.Admin.Documents.UpdateAsync(plain.Id, withLine, plain.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onCreate = (await _p.Admin.Documents.CreateReceiptAsync(withLine)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var onIssue = (await _p.Admin.Documents.CreateIssueAsync(withLine with { PartnerId = _p.Reference.CustomerId, Purpose = IssuePurpose.Sale })).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Messages.ReceiptNotLinked, Assert.Single(onUpdate.ErrorsFor("lines[0].purchaseOrderLineNo")));
        Assert.Equal(Messages.ReceiptNotLinked, Assert.Single(onCreate.ErrorsFor("lines[0].purchaseOrderLineNo")));
        Assert.Equal(Messages.ReceiptNotLinked, Assert.Single(onIssue.ErrorsFor("lines[0].purchaseOrderLineNo")));
        Assert.Null((await _p.Stock.GetAsync(plain.Id)).Lines[0].PurchaseOrderLineNo);

        await _p.TurnOffAsync();
        var off = (await _p.Admin.Documents.UpdateAsync(plain.Id, withLine, plain.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("not available while the purchasing module is off", Assert.Single(off.ErrorsFor("lines[0].purchaseOrderLineNo")));

        // and without the field everything is as before, module on or off
        (await _p.Admin.Documents.UpdateAsync(plain.Id, new DocumentSaveRequest(plain.WarehouseId, plain.PartnerId, null, null, [new DocumentLineInput(_a.Id, 3m, null, 1m, null)]), plain.ConcurrencyToken)).ShouldBeOk();
    }

    [Fact]
    public async Task A_linked_draft_cannot_be_saved_while_the_module_is_off_or_once_the_order_is_no_longer_approved()
    {
        var (order, receipt) = await ReceiptAsync();
        await _p.SetStatusAsync(order, PurchaseOrderStatus.Closed);
        var closed = (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 1m, 1))).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.PurchaseOrderNotReceivable(order.No, PurchaseOrderStatus.Closed), closed.Detail);

        await _p.SetStatusAsync(order, PurchaseOrderStatus.Approved);
        (await SaveAsync(receipt, _p.Clerk, LineFor(_a.Id, 1m, 1))).ShouldBeOk();

        await _p.TurnOffAsync();
        var off = (await SaveAsync(await _p.Stock.GetAsync(receipt.Id), _p.Clerk, LineFor(_a.Id, 1m, 1))).ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(Messages.ReceiptModuleOff, off.Detail);
    }

    [Fact]
    public async Task The_price_is_the_orders_unless_a_caller_who_may_enter_cost_types_another()
    {
        var (_, receipt) = await ReceiptAsync();

        // the Administrator may type a cost; leaving it out keeps the order's price
        var typed = (await SaveAsync(receipt, _p.Admin, LineFor(_a.Id, 10m, 1, 2.75m), LineFor(_b.Id, 5m, 2))).Data<DocumentDto>();
        Assert.Equal([2.75m, 3m], typed.Lines.Select(l => l.UnitCost!.Value));

        // Warehouse staff cannot enter cost: whatever they send, the line is priced at the order's price
        var staff = await _p.ReceiptFromAsync(await _p.ApprovedAsync((_a.Id, 10m, 2m, null)), _p.Staff);
        var forced = (await SaveAsync(staff, _p.Staff, LineFor(_a.Id, 10m, 1, 99m))).Data<DocumentDto>();
        Assert.All(forced.Lines, l => Assert.Null(l.UnitCost));
        var posted = await _p.PostAsync(forced, _p.Staff);
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        var ledger = (await _p.Stock.LedgerAsync(_a.Id, staff.WarehouseId)).Single(r => r.SourceDocId == staff.Id);
        Assert.Equal(2m, ledger.UnitCost);

        // a fraction of a cent is refused for a caller who may type cost, as on any receipt
        (await SaveAsync(await _p.Stock.GetAsync(receipt.Id), _p.Admin, LineFor(_a.Id, 1m, 1, 1.005m))).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Warehouse_staff_save_their_own_linked_draft_and_cannot_edit_another_persons()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var mine = await _p.ReceiptFromAsync(order, _p.Staff);

        (await SaveAsync(mine, _p.Staff, LineFor(_a.Id, 3m, 1))).ShouldBeOk();
        (await SaveAsync(await _p.Stock.GetAsync(mine.Id), _p.Clerk, LineFor(_a.Id, 3m, 1))).ShouldBe(HttpStatusCode.Forbidden); // the document rule: the Clerk edits only their own
    }
}
