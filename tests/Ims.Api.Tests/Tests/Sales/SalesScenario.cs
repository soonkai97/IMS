using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// What every sales-order test needs: the module switched on (the setting row is written straight into the database, so these tests do not depend on how the API switches it), the everyday
/// clients, and a warehouse, a product and a customer of the test's own, so another test's orders never appear in what it counts.
/// </summary>
public sealed class SalesScenario
{
    private SalesScenario(ApiFixture fixture, StockScenario stock)
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

    /// <summary>Switches every module off and closes what the test left open: the check refuses to turn a module off while an order is open, and the database is shared by all test classes.</summary>
    public static async Task ResetAsync(ApiFixture fixture)
    {
        await TurnOffAsync(fixture);
        await RestoreStandardRulesAsync(fixture);
    }

    private static async Task TurnOffAsync(ApiFixture fixture)
    {
        await fixture.Database.ExecuteAsync("UPDATE SalesOrder SET Status = 4 WHERE Status <= 2"); // Cancelled
        await fixture.Database.ExecuteAsync("UPDATE PurchaseOrder SET Status = 4 WHERE Status <= 2");
        await fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");
    }

    /// <summary>The two rules the migration AddSalesOrderApprovalRules writes (5,000.00 for a Supervisor, 20,000.00 for the Owner), restored after a test changed, switched off or added rules.</summary>
    private static async Task RestoreStandardRulesAsync(ApiFixture fixture)
    {
        await fixture.Database.ExecuteAsync("DELETE FROM ApprovalRule WHERE DocType = 6");
        await fixture.Database.ExecuteAsync("INSERT ApprovalRule (DocType, AmountThreshold, QtyThreshold, Tier, IsActive) VALUES (6, 5000, NULL, 1, 1), (6, 20000, NULL, 2, 1)");
    }

    public static async Task<SalesScenario> CreateAsync(ApiFixture fixture, bool moduleOn = true)
    {
        await ResetAsync(fixture);
        if (moduleOn) await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.sales', N'true')");
        return new SalesScenario(fixture, await StockScenario.CreateAsync(fixture));
    }

    public Task TurnOffAsync() => TurnOffAsync(Fixture);

    public Task<WarehouseDto> NewWarehouseAsync(bool isActive = true) => Stock.NewWarehouseAsync(isActive);

    public Task<ProductDto> NewProductAsync(Action<ProductBuilder>? customize = null) => Stock.NewProductAsync(customize: customize);

    public async Task<PartnerDto> NewPartnerAsync(PartnerType type = PartnerType.Customer, bool isActive = true) =>
        (await Admin.Partners.CreateAsync(new PartnerSaveRequest(type, Support.Unique.Name("Partner"), null, isActive))).Data<PartnerDto>();

    /// <summary>A valid one-line request for the warehouse (the default customer, 10 pieces at 2.50).</summary>
    public SalesOrderBuilder Request(Guid warehouseId, Guid productId, Guid? customerId = null) =>
        new SalesOrderBuilder(warehouseId, customerId ?? Reference.CustomerId).WithLine(productId, 10m, 2.50m);

    public async Task<SalesOrderDto> CreateAsync(Guid warehouseId, Guid productId, ApiClient? by = null, Guid? customerId = null) =>
        (await (by ?? Admin).SalesOrders.CreateAsync(Request(warehouseId, productId, customerId).Build())).Data<SalesOrderDto>();

    /// <summary>The default warehouse: the one the Supervisor and the Clerk work in (the Administrator and the Owner see every warehouse).</summary>
    public Guid Warehouse => Reference.DefaultWarehouseId;

    private Application.Features.Catalog.ProductDto? _product;

    /// <summary>A Draft in the default warehouse with one line worth exactly <paramref name="total"/> (one unit at that price); 0 is the unpriced draft.</summary>
    public async Task<SalesOrderDto> DraftWorthAsync(decimal total, ApiClient? by = null)
    {
        _product ??= await NewProductAsync();
        return (await (by ?? Clerk).SalesOrders.CreateAsync(new SalesOrderBuilder(Warehouse, Reference.CustomerId).WithLine(_product.Id, 1m, total).Build())).Data<SalesOrderDto>();
    }

    /// <summary>Creates a Draft worth <paramref name="total"/> and submits it as <paramref name="by"/> (the Clerk by default); the result is Submitted, or Approved when no rule fires.</summary>
    public async Task<SalesOrderDto> SubmittedWorthAsync(decimal total, ApiClient? by = null)
    {
        var maker = by ?? Clerk;
        var draft = await DraftWorthAsync(total, maker);
        return (await maker.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
    }

    public async Task<SalesOrderDto> ReadAsync(Guid id, ApiClient? reader = null) => (await (reader ?? Admin).SalesOrders.GetAsync(id)).Data<SalesOrderDto>();

    public Task SetStatusAsync(SalesOrderDto order, SalesOrderStatus status) =>
        Fixture.Database.ExecuteAsync("UPDATE SalesOrder SET Status = @s WHERE PublicId = @id", ("@s", (int)status), ("@id", order.Id));
}
