using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.2 acceptance 1 to 3 on a real SQL Server: creating a draft (number, lines, base quantity, amounts, one audit event), a gap-free number under parallel creates (BR-11), and every
/// way a request can be wrong, each answered with the field it belongs to and all lines at once.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderCreateTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private static string YearMonth => DocumentNumber.YearMonthOf(DateTime.UtcNow).ToString(DocumentNumberFormat.YearMonthFormat);

    private async Task<ProblemBody> RefusedAsync(PurchaseOrderSaveRequest request)
    {
        var before = await CountAsync();
        var problem = (await _p.Admin.PurchaseOrders.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(before, await CountAsync()); // a refused request writes nothing
        return problem;
    }

    private Task<int> CountAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder");

    // ----- acceptance 1 -----
    [Fact]
    public async Task A_caller_with_po_manage_creates_a_draft_with_the_next_number_numbered_lines_converted_quantities_and_rounded_amounts()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 12m));
        var request = new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId).WithRemark("urgent")
            .WithLine(product.Id, 3m, 120m, fixture.Reference.CartonUnitId)   // 3 cartons = 36 pieces, 3 x 120.00 = 360.00 whatever the carton holds
            .WithLine(product.Id, 1.5m, 0.01m)                                // 0.015 rounds half away from zero to 0.02
            .Build();

        var response = await _p.Admin.PurchaseOrders.CreateAsync(request);
        var order = response.Data<PurchaseOrderDto>();

        Assert.Equal($"PO-{warehouse.Code}-{YearMonth}-0001", order.No);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal((warehouse.Id, fixture.Reference.SupplierId, "urgent"), (order.WarehouseId, order.SupplierId, order.Remark));
        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNo));
        Assert.Equal([36m, 1.5m], order.Lines.Select(l => l.BaseQty));
        Assert.Equal([3m, 1.5m], order.Lines.Select(l => l.Qty));
        Assert.Equal([120m, 0.01m], order.Lines.Select(l => l.UnitPrice!.Value));
        Assert.Equal([360m, 0.02m], order.Lines.Select(l => l.Amount!.Value));
        Assert.Equal(360.02m, order.TotalAmount);
        Assert.Equal(TestUsers.UsernameOf(Roles.Admin), order.CreatedBy);
        Assert.Equal($"\"{order.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.NotEmpty(order.ConcurrencyToken);
    }

    [Fact]
    public async Task Creating_writes_exactly_one_audit_event_naming_the_order_by_its_number_and_no_money_a_reader_without_cost_view_could_see()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();

        var order = await _p.CreateAsync(warehouse.Id, product.Id);

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @no",
            ("@a", AuditActions.PurchaseOrderCreated), ("@t", AuditEntities.PurchaseOrder), ("@no", order.No)));
        var after = await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE EntityId = @no AND Action = @a", ("@no", order.No), ("@a", AuditActions.PurchaseOrderCreated));
        Assert.Equal(25m, System.Text.Json.JsonDocument.Parse(after).RootElement.GetProperty(AuditFields.Amount).GetDecimal()); // the key the audit page withholds from a caller without cost.view
    }

    [Fact]
    public async Task The_numbers_of_one_warehouse_and_month_run_on_and_every_warehouse_has_its_own_sequence()
    {
        var first = await _p.NewWarehouseAsync();
        var second = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();

        var a1 = await _p.CreateAsync(first.Id, product.Id);
        var a2 = await _p.CreateAsync(first.Id, product.Id);
        var b1 = await _p.CreateAsync(second.Id, product.Id);

        Assert.Equal([$"PO-{first.Code}-{YearMonth}-0001", $"PO-{first.Code}-{YearMonth}-0002", $"PO-{second.Code}-{YearMonth}-0001"], [a1.No, a2.No, b1.No]);
    }

    [Fact]
    public async Task A_purchase_order_and_a_receipt_of_the_same_warehouse_and_month_do_not_share_a_sequence()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();

        var receipt = await _p.Stock.DraftReceiptAsync(_p.Admin, warehouse.Id, new Line(product.Id, 1m, 1m));
        var order = await _p.CreateAsync(warehouse.Id, product.Id);

        Assert.Equal($"RCV-{warehouse.Code}-{YearMonth}-0001", receipt.No);
        Assert.Equal($"PO-{warehouse.Code}-{YearMonth}-0001", order.No);
    }

    // ----- acceptance 2 -----
    [Fact]
    public async Task Twenty_parallel_creates_in_one_warehouse_and_month_get_distinct_gap_free_numbers()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var request = _p.Request(warehouse.Id, product.Id).Build();

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 20).Select(_ => (Func<Task<ApiResponse>>)(() => _p.Admin.PurchaseOrders.CreateAsync(request))));

        var numbers = responses.Select(r => r.Data<PurchaseOrderDto>().No).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"PO-{warehouse.Code}-{YearMonth}-{n:D4}"), numbers);
        Assert.Equal(20, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder WHERE No LIKE @p", ("@p", $"PO-{warehouse.Code}-%")));
    }

    // ----- acceptance 3 -----
    [Fact]
    public async Task A_customer_cannot_be_the_supplier_and_an_inactive_supplier_or_warehouse_or_product_is_refused()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var customer = await _p.NewPartnerAsync(PartnerType.Customer);
        var inactiveSupplier = await _p.NewPartnerAsync(isActive: false);
        var inactiveWarehouse = await _p.NewWarehouseAsync(isActive: false);
        var inactiveProduct = await _p.NewProductAsync(b => b.Inactive());

        Assert.Equal(Messages.SupplierRequired, (await RefusedAsync(_p.Request(warehouse.Id, product.Id, customer.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerInactive, (await RefusedAsync(_p.Request(warehouse.Id, product.Id, inactiveSupplier.Id).Build())).Detail);
        Assert.Equal(Messages.WarehouseInactive, (await RefusedAsync(_p.Request(inactiveWarehouse.Id, product.Id).Build())).Detail);
        var line = (await RefusedAsync(_p.Request(warehouse.Id, inactiveProduct.Id).Build())).Errors!;
        Assert.Contains(Messages.ProductInactive(inactiveProduct.Sku), line["lines[0]"]);
    }

    [Fact]
    public async Task A_warehouse_a_supplier_or_a_product_that_does_not_exist_is_a_400_that_says_so()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();

        Assert.Equal(Messages.WarehouseMissing, (await RefusedAsync(_p.Request(Guid.NewGuid(), product.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerMissing, (await RefusedAsync(_p.Request(warehouse.Id, product.Id, Guid.NewGuid()).Build())).Detail);
        Assert.Contains("lines[0]", (await RefusedAsync(_p.Request(warehouse.Id, Guid.NewGuid()).Build())).Errors!.Keys);
    }

    [Fact]
    public async Task An_empty_order_and_an_order_with_too_many_lines_are_refused()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();

        var none = await RefusedAsync(new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId).Build());
        var builder = new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId);
        for (var i = 0; i <= Limits.MaxDocumentLines; i++) builder.WithLine(product.Id, 1m, 1m);
        var many = await RefusedAsync(builder.Build());

        Assert.Contains(Messages.AddLine, none.ErrorsFor("lines"));
        Assert.Contains(Messages.TooManyLines(Limits.MaxDocumentLines), many.ErrorsFor("lines"));
    }

    [Fact]
    public async Task A_quantity_a_price_or_an_amount_above_the_limits_and_a_fraction_of_a_cent_each_name_their_field()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        PurchaseOrderSaveRequest Order(decimal qty, decimal price) => new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId).WithLine(product.Id, qty, price).Build();

        Assert.Contains(Messages.QtyTooLarge, (await RefusedAsync(Order(Limits.MaxQuantity + 1, 1m))).ErrorsFor("lines[0].qty"));
        Assert.Contains(Messages.QtyTooLarge, (await RefusedAsync(Order(1.00001m, 1m))).ErrorsFor("lines[0].qty"));
        Assert.Contains(Messages.QtyPositive, (await RefusedAsync(Order(0m, 1m))).ErrorsFor("lines[0].qty"));
        Assert.Contains(Messages.PriceTooLarge, (await RefusedAsync(Order(1m, Limits.MaxUnitCost + 1))).ErrorsFor("lines[0].unitPrice"));
        Assert.Contains(Messages.PriceNegative, (await RefusedAsync(Order(1m, -0.01m))).ErrorsFor("lines[0].unitPrice"));
        Assert.Contains(Messages.PriceTooPrecise, (await RefusedAsync(Order(1m, 1.005m))).ErrorsFor("lines[0].unitPrice"));
        Assert.Contains(Messages.LineAmountTooLarge, (await RefusedAsync(Order(Limits.MaxQuantity, Limits.MaxUnitCost))).ErrorsFor("lines[0].unitPrice"));
    }

    [Fact]
    public async Task Every_wrong_line_is_reported_not_only_the_first()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var inactive = await _p.NewProductAsync(b => b.Inactive());
        var request = new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId)
            .WithLine(product.Id, 0m, 1m)               // validator: quantity
            .WithLine(product.Id, 1m, 1.005m)           // validator: price
            .WithLine(inactive.Id, 1m, 1m)              // handler: product
            .WithLine(product.Id, 1m, 1m, fixture.Reference.CartonUnitId) // handler: the product has no carton conversion
            .Build();

        // The shape is judged first (a validator), the rules that need the database after it; each answer lists all of its own errors.
        var shape = await RefusedAsync(request);
        Assert.NotEmpty(shape.ErrorsFor("lines[0].qty"));
        Assert.NotEmpty(shape.ErrorsFor("lines[1].unitPrice"));

        var rules = await RefusedAsync(new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId).WithLine(inactive.Id, 1m, 1m).WithLine(product.Id, 1m, 1m)
            .WithLine(product.Id, 1m, 1m, fixture.Reference.CartonUnitId).Build());
        Assert.Equal(["lines[0]", "lines[2]"], rules.Errors!.Keys.Order());
    }

    [Fact]
    public async Task A_null_line_is_a_400_not_a_500()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var json = $$"""{"warehouseId":"{{warehouse.Id}}","supplierId":"{{fixture.Reference.SupplierId}}","lines":[null]}""";

        var problem = (await _p.Admin.PostRawJsonAsync("/api/purchase-orders", json)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor("lines[0]"));
    }

    /// <summary>Security retest F-3: a purchase order carries cost data, so no shared cache may keep it.</summary>
    [Fact]
    public async Task A_purchase_order_response_is_private_and_no_store()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var order = (await _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId).WithLine(product.Id, 1m, 5m).Build())).Data<PurchaseOrderDto>();

        Assert.Equal(["no-store", "private"], (await _p.Admin.PurchaseOrders.GetAsync(order.Id)).ShouldBeOk().CacheDirectives);
        Assert.Equal(["no-store", "private"], (await _p.Admin.PurchaseOrders.ListAsync()).ShouldBeOk().CacheDirectives);
    }

    /// <summary>Security retest F-1: lines of the wrong JSON type are a 400 that names no CLR type, JSON path or byte position.</summary>
    [Fact]
    public async Task Lines_of_the_wrong_JSON_type_are_a_400_that_names_no_framework_internals()
    {
        var response = await _p.Admin.PostRawJsonAsync("/api/purchase-orders", "{\"lines\":{}}");

        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Messages.RequestNotValid, problem.Detail);
        Assert.NotEmpty(problem.Errors!);
        foreach (var internals in new[] { "Ims.", "System.", "LineNumber", "Path:" }) Assert.DoesNotContain(internals, response.Body);
    }

    [Fact]
    public async Task A_price_of_zero_is_accepted_on_a_draft_and_a_unit_the_product_converts_is_accepted()
    {
        var warehouse = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 6m));

        var order = (await _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(warehouse.Id, fixture.Reference.SupplierId)
            .WithLine(product.Id, 2m, 0m, fixture.Reference.CartonUnitId).Build())).Data<PurchaseOrderDto>();

        Assert.Equal((12m, 0m, 0m), (order.Lines[0].BaseQty, order.Lines[0].Amount!.Value, order.TotalAmount!.Value));
    }
}
