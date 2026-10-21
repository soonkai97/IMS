using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Api.Tests.Tests.Purchasing;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Import;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>
/// A unit conversion that stock movements or purchase orders used cannot change or go away (decided 2026-10-21): a purchase-order line freezes its base quantity at order time, a receipt line computes it at
/// save time, so a changed factor would leave an order that can never be received exactly. Adding a conversion, re-sending an unchanged one and changing an unused one stay allowed.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConversionLockTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] ImportHeaders = ["SKU", "Name", "Category", "Unit", "SafetyStock", "Barcodes"];

    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ReferenceData Ref => fixture.Reference;

    private Task<ProductDto> CartonProductAsync(decimal factor = 12m) => _p.NewProductAsync(b => b.WithConversion(Ref.CartonUnitId, factor));

    /// <summary>The product as the web form re-saves it: every field as the API returned it, the conversions replaced by <paramref name="conversions"/>.</summary>
    private ProductBuilder Resave(ProductDto product, params (Guid Unit, decimal Factor)[] conversions)
    {
        var builder = ProductBuilder.From(Ref, product with { Conversions = [] });
        foreach (var (unit, factor) in conversions) builder.WithConversion(unit, factor);
        return builder;
    }

    private Task<ApiResponse> SaveAsync(ProductDto product, params (Guid Unit, decimal Factor)[] conversions) =>
        _p.Admin.Products.UpdateAsync(product.Id, Resave(product, conversions).Build());

    private async Task<ProductDto> ReadAsync(ProductDto product) => (await _p.Admin.Products.GetAsync(product.Id)).Data<ProductDto>();

    private async Task<decimal> CartonFactorAsync(ProductDto product) => (await ReadAsync(product)).Conversions.Single(c => c.FromUomId == Ref.CartonUnitId).Factor;

    private Task<DocumentDto> ReceiveAsync(ProductDto product) => _p.Stock.ReceiveAsync(_p.Warehouse, new Line(product.Id, 5, 1m));

    private async Task<PurchaseOrderDto> OrderCartonsAsync(ProductDto product) =>
        (await _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, Ref.SupplierId).WithLine(product.Id, 3m, 10m, Ref.CartonUnitId).Build())).Data<PurchaseOrderDto>();

    private async Task AssertLockedAsync(ApiResponse response, ProductDto product, decimal expectedFactor)
    {
        var problem = response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Messages.ConversionLocked("CTN"), problem.Detail);
        Assert.Equal(expectedFactor, await CartonFactorAsync(product)); // nothing was written
    }

    // ----- never used -----
    [Fact]
    public async Task A_product_that_never_moved_and_is_on_no_order_can_change_and_drop_a_factor()
    {
        var product = await CartonProductAsync();

        (await SaveAsync(product, (Ref.CartonUnitId, 10m))).ShouldBeOk();
        Assert.Equal(10m, await CartonFactorAsync(product));
        (await SaveAsync(product)).ShouldBeOk();
        Assert.Empty((await ReadAsync(product)).Conversions);
    }

    // ----- stock movements -----
    [Fact]
    public async Task After_a_posted_receipt_the_factor_cannot_change_and_the_conversion_cannot_be_removed()
    {
        var product = await CartonProductAsync();
        await ReceiveAsync(product);

        await AssertLockedAsync(await SaveAsync(product, (Ref.CartonUnitId, 10m)), product, 12m);
        await AssertLockedAsync(await SaveAsync(product), product, 12m);
    }

    [Fact]
    public async Task After_only_a_reversed_movement_it_is_still_locked()
    {
        var product = await CartonProductAsync();
        var receipt = await ReceiveAsync(product);
        (await _p.Admin.Documents.ReverseAsync(receipt.Id, "entered by mistake")).ShouldBeOk();

        await AssertLockedAsync(await SaveAsync(product, (Ref.CartonUnitId, 15m)), product, 12m);
    }

    // ----- purchase orders -----
    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Closed)]
    public async Task A_non_cancelled_order_line_in_the_unit_locks_it_and_the_refusal_names_the_unit_not_the_order(PurchaseOrderStatus status)
    {
        var product = await CartonProductAsync();
        var order = await OrderCartonsAsync(product);
        await _p.SetStatusAsync(order, status);

        var response = await SaveAsync(product, (Ref.CartonUnitId, 10m));

        await AssertLockedAsync(response, product, 12m);
        Assert.DoesNotContain(order.No, response.Body);
    }

    [Fact]
    public async Task A_cancelled_order_does_not_lock()
    {
        var product = await CartonProductAsync();
        await _p.SetStatusAsync(await OrderCartonsAsync(product), PurchaseOrderStatus.Cancelled);

        (await SaveAsync(product, (Ref.CartonUnitId, 10m))).ShouldBeOk();
    }

    [Fact]
    public async Task An_order_line_in_another_unit_does_not_lock_the_conversion()
    {
        var product = await CartonProductAsync();
        (await _p.Admin.PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(_p.Warehouse, Ref.SupplierId).WithLine(product.Id, 30m, 1m).Build())).ShouldBeOk();

        (await SaveAsync(product, (Ref.CartonUnitId, 10m))).ShouldBeOk();
    }

    // ----- always allowed -----
    [Fact]
    public async Task Adding_a_conversion_and_re_sending_an_unchanged_one_are_allowed_on_a_used_product()
    {
        var product = await CartonProductAsync(12.5m);
        await ReceiveAsync(product);

        (await SaveAsync(product, (Ref.CartonUnitId, 12.5m), (Ref.KilogramUnitId, 1000m))).ShouldBeOk();

        var saved = await ReadAsync(product);
        Assert.Equal([12.5m, 1000m], saved.Conversions.Select(c => c.Factor).Order());
        // The web form re-sends the whole product exactly as the API returned it.
        (await _p.Admin.Products.UpdateAsync(saved.Id, ProductBuilder.From(Ref, saved).Build())).ShouldBeOk();
    }

    [Fact]
    public async Task A_factor_re_sent_with_more_decimals_than_the_column_keeps_is_not_a_change()
    {
        var product = await CartonProductAsync(0.333333m);
        await ReceiveAsync(product);

        (await SaveAsync(product, (Ref.CartonUnitId, 0.3333330001m))).ShouldBeOk();
        await AssertLockedAsync(await SaveAsync(product, (Ref.CartonUnitId, 0.333334m)), product, 0.333333m);
    }

    [Fact]
    public async Task Changing_the_base_unit_of_a_product_whose_conversion_is_on_an_order_is_refused_and_keeps_the_conversion()
    {
        var product = await CartonProductAsync();
        await OrderCartonsAsync(product);

        var request = ProductBuilder.From(Ref, product with { Conversions = [] }).WithBaseUnit(Ref.KilogramUnitId).Build();
        (await _p.Admin.Products.UpdateAsync(product.Id, request)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(12m, await CartonFactorAsync(product));
    }

    // ----- import -----
    [Fact]
    public async Task The_import_keeps_the_unit_of_a_product_whose_conversion_is_in_use_and_changes_one_whose_is_not()
    {
        var used = await CartonProductAsync();
        await OrderCartonsAsync(used);
        var free = await CartonProductAsync();
        var file = Xlsx.Create("Products", ImportHeaders, [used.Sku, "Renamed", "General", "KG", 2, null], [free.Sku, "Renamed", "General", "KG", 2, null]);

        var result = (await _p.Admin.Import.ProductsAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal(Messages.UnitNotChangedConversionsUsed, Assert.Single(result.Rows[0].Errors));
        Assert.Empty(result.Rows[1].Errors);
        var usedAfter = await ReadAsync(used);
        Assert.Equal(("PCS", 12m), (usedAfter.BaseUomCode, usedAfter.Conversions.Single().Factor));
        var freeAfter = await ReadAsync(free);
        Assert.Equal("KG", freeAfter.BaseUomCode);
        Assert.Empty(freeAfter.Conversions);
    }

    // ----- access unchanged -----
    [Fact]
    public async Task A_caller_without_master_data_manage_is_still_refused_before_the_rule_runs()
    {
        var product = await CartonProductAsync();
        await ReceiveAsync(product);

        (await _p.Clerk.Products.UpdateAsync(product.Id, Resave(product, (Ref.CartonUnitId, 10m)).Build())).ShouldBe(HttpStatusCode.Forbidden);
    }
}
