using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// What every purchase-order test needs: the module switched on (the setting row is written straight into the database, so these tests do not depend on how the API switches it), the
/// everyday clients, and a warehouse, a product and a supplier of the test's own, so another test's orders never appear in what it counts.
/// </summary>
public sealed class PurchasingScenario
{
    private PurchasingScenario(ApiFixture fixture, StockScenario stock)
    {
        Fixture = fixture;
        Stock = stock;
    }

    public ApiFixture Fixture { get; }
    public StockScenario Stock { get; }
    public ApiClient Admin => Stock.Admin;
    public ApiClient Clerk => Stock.Clerk;
    public ApiClient Supervisor => Stock.Supervisor;
    public ApiClient Owner => Stock.Owner;
    public ApiClient Staff => Stock.Staff;
    public ReferenceData Reference => Fixture.Reference;

    public static Task ResetAsync(ApiFixture fixture) => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    public static async Task<PurchasingScenario> CreateAsync(ApiFixture fixture, bool moduleOn = true)
    {
        await ResetAsync(fixture);
        if (moduleOn) await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.purchasing', N'true')");
        return new PurchasingScenario(fixture, await StockScenario.CreateAsync(fixture));
    }

    public Task TurnOffAsync() => ResetAsync(Fixture);

    public Task<WarehouseDto> NewWarehouseAsync(bool isActive = true) => Stock.NewWarehouseAsync(isActive);

    public Task<ProductDto> NewProductAsync(Action<ProductBuilder>? customize = null) => Stock.NewProductAsync(customize: customize);

    public async Task<PartnerDto> NewPartnerAsync(PartnerType type = PartnerType.Supplier, bool isActive = true) =>
        (await Admin.Partners.CreateAsync(new PartnerSaveRequest(type, Support.Unique.Name("Partner"), null, isActive))).Data<PartnerDto>();

    /// <summary>A valid one-line request for the warehouse (the default supplier, 10 pieces at 2.50).</summary>
    public PurchaseOrderBuilder Request(Guid warehouseId, Guid productId, Guid? supplierId = null) =>
        new PurchaseOrderBuilder(warehouseId, supplierId ?? Reference.SupplierId).WithLine(productId, 10m, 2.50m);

    public async Task<PurchaseOrderDto> CreateAsync(Guid warehouseId, Guid productId, ApiClient? by = null, Guid? supplierId = null) =>
        (await (by ?? Admin).PurchaseOrders.CreateAsync(Request(warehouseId, productId, supplierId).Build())).Data<PurchaseOrderDto>();

    public Task SetStatusAsync(PurchaseOrderDto order, PurchaseOrderStatus status) =>
        Fixture.Database.ExecuteAsync("UPDATE PurchaseOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)status), ("@id", order.Id));

    private Application.Features.Catalog.ProductDto? _product;

    /// <summary>The default warehouse: the one the Supervisor and the Clerk work in (the Administrator and the Owner see every warehouse).</summary>
    public Guid Warehouse => Reference.DefaultWarehouseId;

    /// <summary>A Draft in the default warehouse with one line worth exactly <paramref name="total"/> (one unit at that price); 0 is the unpriced draft a reorder would start from.</summary>
    public async Task<PurchaseOrderDto> DraftWorthAsync(decimal total, ApiClient? by = null)
    {
        _product ??= await NewProductAsync();
        return (await (by ?? Clerk).PurchaseOrders.CreateAsync(new PurchaseOrderBuilder(Warehouse, Reference.SupplierId).WithLine(_product.Id, 1m, total).Build())).Data<PurchaseOrderDto>();
    }

    /// <summary>Creates a Draft worth <paramref name="total"/> and submits it as <paramref name="by"/> (the Clerk by default); the result is Submitted, or Approved when no rule fires.</summary>
    public async Task<PurchaseOrderDto> SubmittedWorthAsync(decimal total, ApiClient? by = null)
    {
        var maker = by ?? Clerk;
        var draft = await DraftWorthAsync(total, maker);
        return (await maker.PurchaseOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<PurchaseOrderDto>();
    }

    public async Task<PurchaseOrderDto> ReadAsync(Guid id, ApiClient? reader = null) => (await (reader ?? Admin).PurchaseOrders.GetAsync(id)).Data<PurchaseOrderDto>();
}
