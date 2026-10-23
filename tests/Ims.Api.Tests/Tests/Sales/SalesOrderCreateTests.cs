using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.2 acceptance 1 to 3 on a real SQL Server: creating a draft (number, lines, base quantity, amounts, one audit event), a gap-free number under parallel creates (BR-11), and every
/// way a request can be wrong, each answered with the field it belongs to and all lines at once.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderCreateTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private static string YearMonth => DocumentNumber.YearMonthOf(DateTime.UtcNow).ToString(DocumentNumberFormat.YearMonthFormat);

    private async Task<ProblemBody> RefusedAsync(SalesOrderSaveRequest request)
    {
        var before = await CountAsync();
        var problem = (await _s.Admin.SalesOrders.CreateAsync(request)).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(before, await CountAsync()); // a refused request writes nothing
        return problem;
    }

    private Task<int> CountAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder");

    [Fact]
    public async Task A_caller_with_so_manage_creates_a_draft_with_the_next_number_numbered_lines_converted_quantities_and_rounded_amounts()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 12m));
        var request = new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId).WithRemark("urgent")
            .WithLine(product.Id, 3m, 120m, fixture.Reference.CartonUnitId)   // 3 cartons = 36 pieces, 3 x 120.00 = 360.00 whatever the carton holds
            .WithLine(product.Id, 1.5m, 0.01m)                                // 0.015 rounds half away from zero to 0.02
            .Build();

        var response = await _s.Admin.SalesOrders.CreateAsync(request);
        var order = response.Data<SalesOrderDto>();

        Assert.Equal($"SO-{warehouse.Code}-{YearMonth}-0001", order.No);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.Equal((warehouse.Id, fixture.Reference.CustomerId, "urgent"), (order.WarehouseId, order.CustomerId, order.Remark));
        Assert.Equal([1, 2], order.Lines.Select(l => l.LineNo));
        Assert.Equal([36m, 1.5m], order.Lines.Select(l => l.BaseQty));
        Assert.Equal([120m, 0.01m], order.Lines.Select(l => l.UnitPrice!.Value));
        Assert.Equal([360m, 0.02m], order.Lines.Select(l => l.Amount!.Value));
        Assert.Equal(360.02m, order.TotalAmount);
        Assert.Equal(37.5m, order.TotalQty);
        Assert.Equal(TestUsers.UsernameOf(Roles.Admin), order.CreatedBy);
        Assert.Equal($"\"{order.ConcurrencyToken}\"", response.Header("ETag"));
    }

    [Fact]
    public async Task Creating_writes_exactly_one_audit_event_naming_the_order_by_its_number()
    {
        var warehouse = await _s.NewWarehouseAsync();

        var order = await _s.CreateAsync(warehouse.Id, (await _s.NewProductAsync()).Id);

        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @no",
            ("@a", AuditActions.SalesOrderCreated), ("@t", AuditEntities.SalesOrder), ("@no", order.No)));
    }

    [Fact]
    public async Task The_numbers_of_one_warehouse_and_month_run_on_and_a_sales_order_does_not_share_a_sequence_with_a_receipt()
    {
        var first = await _s.NewWarehouseAsync();
        var second = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();

        var a1 = await _s.CreateAsync(first.Id, product.Id);
        var a2 = await _s.CreateAsync(first.Id, product.Id);
        var b1 = await _s.CreateAsync(second.Id, product.Id);
        var receipt = await _s.Stock.DraftReceiptAsync(_s.Admin, second.Id, new Line(product.Id, 1m, 1m));

        Assert.Equal([$"SO-{first.Code}-{YearMonth}-0001", $"SO-{first.Code}-{YearMonth}-0002", $"SO-{second.Code}-{YearMonth}-0001"], [a1.No, a2.No, b1.No]);
        Assert.Equal($"RCV-{second.Code}-{YearMonth}-0001", receipt.No);
    }

    [Fact]
    public async Task Twenty_parallel_creates_in_one_warehouse_and_month_get_distinct_gap_free_numbers()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var request = _s.Request(warehouse.Id, product.Id).Build();

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 20).Select(_ => (Func<Task<ApiResponse>>)(() => _s.Admin.SalesOrders.CreateAsync(request))));

        var numbers = responses.Select(r => r.Data<SalesOrderDto>().No).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 20).Select(n => $"SO-{warehouse.Code}-{YearMonth}-{n:D4}"), numbers);
        Assert.Equal(20, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE No LIKE @p", ("@p", $"SO-{warehouse.Code}-%")));
    }

    [Fact]
    public async Task A_supplier_cannot_be_the_customer_and_an_inactive_customer_or_warehouse_or_product_is_refused()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var supplier = await _s.NewPartnerAsync(PartnerType.Supplier);
        var inactiveCustomer = await _s.NewPartnerAsync(isActive: false);
        var inactiveWarehouse = await _s.NewWarehouseAsync(isActive: false);
        var inactiveProduct = await _s.NewProductAsync(b => b.Inactive());

        Assert.Equal(Messages.PartnerWrongType(DocumentType.Issue, PartnerType.Customer), (await RefusedAsync(_s.Request(warehouse.Id, product.Id, supplier.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerInactive, (await RefusedAsync(_s.Request(warehouse.Id, product.Id, inactiveCustomer.Id).Build())).Detail);
        Assert.Equal(Messages.WarehouseInactive, (await RefusedAsync(_s.Request(inactiveWarehouse.Id, product.Id).Build())).Detail);
        var line = (await RefusedAsync(_s.Request(warehouse.Id, inactiveProduct.Id).Build())).Errors!;
        Assert.Contains(Messages.ProductInactive(inactiveProduct.Sku), line["lines[0]"]);
    }

    [Fact]
    public async Task A_warehouse_a_customer_or_a_product_that_does_not_exist_is_a_400_that_says_so()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();

        Assert.Equal(Messages.WarehouseMissing, (await RefusedAsync(_s.Request(Guid.NewGuid(), product.Id).Build())).Detail);
        Assert.Equal(Messages.PartnerMissing, (await RefusedAsync(_s.Request(warehouse.Id, product.Id, Guid.NewGuid()).Build())).Detail);
        Assert.Contains("lines[0]", (await RefusedAsync(_s.Request(warehouse.Id, Guid.NewGuid()).Build())).Errors!.Keys);
    }

    [Fact]
    public async Task An_empty_order_and_an_order_with_too_many_lines_are_refused()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();

        var none = await RefusedAsync(new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId).Build());
        var builder = new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId);
        for (var i = 0; i <= Limits.MaxDocumentLines; i++) builder.WithLine(product.Id, 1m, 1m);
        var many = await RefusedAsync(builder.Build());

        Assert.Contains(Messages.AddLine, none.ErrorsFor("lines"));
        Assert.Contains(Messages.TooManyLines(Limits.MaxDocumentLines), many.ErrorsFor("lines"));
    }

    [Fact]
    public async Task A_quantity_a_price_or_an_amount_above_the_limits_and_a_fraction_of_a_cent_each_name_their_field()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        SalesOrderSaveRequest Order(decimal qty, decimal price) => new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId).WithLine(product.Id, qty, price).Build();

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
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var inactive = await _s.NewProductAsync(b => b.Inactive());

        var shape = await RefusedAsync(new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId).WithLine(product.Id, 0m, 1m).WithLine(product.Id, 1m, 1.005m).Build());
        Assert.NotEmpty(shape.ErrorsFor("lines[0].qty"));
        Assert.NotEmpty(shape.ErrorsFor("lines[1].unitPrice"));

        var rules = await RefusedAsync(new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId).WithLine(inactive.Id, 1m, 1m).WithLine(product.Id, 1m, 1m)
            .WithLine(product.Id, 1m, 1m, fixture.Reference.CartonUnitId).Build()); // the product has no carton conversion
        Assert.Equal(["lines[0]", "lines[2]"], rules.Errors!.Keys.Order());
    }

    [Fact]
    public async Task A_null_line_is_a_400_not_a_500()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var json = $$"""{"warehouseId":"{{warehouse.Id}}","customerId":"{{fixture.Reference.CustomerId}}","lines":[null]}""";

        var problem = (await _s.Admin.PostRawJsonAsync("/api/sales-orders", json)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor("lines[0]"));
    }

    [Fact]
    public async Task A_sales_order_response_is_private_and_no_store()
    {
        var order = await _s.CreateAsync((await _s.NewWarehouseAsync()).Id, (await _s.NewProductAsync()).Id);

        Assert.Equal(["no-store", "private"], (await _s.Admin.SalesOrders.GetAsync(order.Id)).ShouldBeOk().CacheDirectives);
        Assert.Equal(["no-store", "private"], (await _s.Admin.SalesOrders.ListAsync()).ShouldBeOk().CacheDirectives);
    }

    [Fact]
    public async Task A_price_of_zero_is_accepted_on_a_draft_and_a_unit_the_product_converts_is_accepted()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync(b => b.WithConversion(fixture.Reference.CartonUnitId, 6m));

        var order = (await _s.Admin.SalesOrders.CreateAsync(new SalesOrderBuilder(warehouse.Id, fixture.Reference.CustomerId)
            .WithLine(product.Id, 2m, 0m, fixture.Reference.CartonUnitId).Build())).Data<SalesOrderDto>();

        Assert.Equal((12m, 0m, 0m), (order.Lines[0].BaseQty, order.Lines[0].Amount!.Value, order.TotalAmount!.Value));
    }
}
