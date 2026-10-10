using Ims.Api.Tests.Clients;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Harness;

/// <summary>Ids of the seeded reference data every test builds on (units, the default warehouse, demo partners and products).</summary>
public sealed record ReferenceData(
    int PieceUnitId, int CartonUnitId, int KilogramUnitId, int DefaultCategoryId,
    int DefaultWarehouseId, string DefaultWarehouseCode, int SupplierId, int CustomerId, int DemoProductId, int DemoProduct2Id)
{
    public static async Task<ReferenceData> LoadAsync(ApiClient admin)
    {
        var units = (await admin.Units.ListAsync()).Data<List<UomDto>>();
        var categories = (await admin.Categories.ListAsync()).Data<List<CategoryDto>>();
        var warehouses = (await admin.Warehouses.ListAsync()).Data<List<WarehouseDto>>();
        var partners = (await admin.Partners.ListAsync()).Data<Ims.Application.PagedResult<PartnerDto>>().Items;
        var demo = (await admin.Products.ListAsync("DEMO-")).Data<Ims.Application.PagedResult<ProductDto>>().Items;
        var warehouse = warehouses.First(w => w.Code == "WH01");

        return new ReferenceData(
            units.First(u => u.Code == "PCS").Id, units.First(u => u.Code == "CTN").Id, units.First(u => u.Code == "KG").Id,
            categories.First().Id, warehouse.Id, warehouse.Code,
            partners.First(p => p.Type == PartnerType.Supplier).Id, partners.First(p => p.Type == PartnerType.Customer).Id,
            demo.First(p => p.Sku == "DEMO-001").Id, demo.First(p => p.Sku == "DEMO-002").Id);
    }
}
