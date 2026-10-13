using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Users;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan PR-4: warehouses, partners, categories and units are addressed by their public id everywhere the API names them: their own routes and
/// responses, and every field and filter of another entity that refers to them. The integer ids stay inside.
/// </summary>
[Collection(ApiCollection.Name)]
public class LeavesFlipTests(ApiFixture fixture)
{
    private const int MaxWaitedPageSize = 200;

    private async Task<Guid> PublicIdAsync(string table, string column, string value) =>
        await fixture.Database.ScalarAsync<Guid>($"SELECT PublicId FROM {table} WHERE {column} = @v", ("@v", value));

    [Fact]
    public async Task A_created_warehouse_partner_category_and_unit_answer_with_the_public_id_stored_for_them()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var warehouse = await admin.Warehouses.CreateAsync(new WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, true));
        var partner = await admin.Partners.CreateAsync(new PartnerSaveRequest(PartnerType.Supplier, Unique.Name("Partner"), null, true));
        var category = await admin.Categories.CreateAsync(Unique.Name("Cat"));
        var unit = await admin.Units.CreateAsync(Unique.Code("U"), "Test unit");

        var w = warehouse.Data<WarehouseDto>();
        var p = partner.Data<PartnerDto>();
        var c = category.Data<CategoryDto>();
        var u = unit.Data<UomDto>();
        Assert.Equal(await PublicIdAsync("Warehouse", "Code", w.Code), w.Id);
        Assert.Equal(await PublicIdAsync("Partner", "Name", p.Name), p.Id);
        Assert.Equal(await PublicIdAsync("Category", "Name", c.Name), c.Id);
        Assert.Equal(await PublicIdAsync("Uom", "Code", u.Code), u.Id);
        foreach (var response in new[] { warehouse, partner, category, unit })
            Assert.Equal(JsonValueKind.String, response.Json().RootElement.GetProperty("id").ValueKind); // a string on the wire, never a number
    }

    [Theory]
    [InlineData("warehouses")]
    [InlineData("partners")]
    [InlineData("categories")]
    [InlineData("units")]
    public async Task The_integer_route_of_these_entities_is_gone(string route)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        (await admin.PutAsync($"/api/{route}/1", new { })).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_document_names_its_warehouse_partner_and_units_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var carton = s.Reference.CartonUnitId;
        var product = await s.NewProductAsync(customize: b => b.WithConversion(carton, 12m));

        var draft = await s.DraftReceiptAsync(s.Admin, s.Reference.DefaultWarehouseId, new Line(product.Id, 2, 1m, carton));
        var read = (await s.Admin.Documents.GetAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal((s.Reference.DefaultWarehouseId, s.Reference.SupplierId), (read.WarehouseId, read.PartnerId));
        Assert.Equal(carton, Assert.Single(read.Lines).UomId);
        Assert.Equal(24m, read.Lines[0].BaseQty);
    }

    [Fact]
    public async Task A_product_names_its_category_unit_and_conversions_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var category = (await s.Admin.Categories.CreateAsync(Unique.Name("Cat"))).Data<CategoryDto>();

        var product = await s.NewProductAsync(customize: b => b.WithCategory(category.Id).WithConversion(s.Reference.CartonUnitId, 6m));

        Assert.Equal((category.Id, s.Reference.PieceUnitId), (product.CategoryId, product.BaseUomId));
        Assert.Equal(new UomConversionDto(s.Reference.CartonUnitId, s.Reference.PieceUnitId, 6m), Assert.Single(product.Conversions));
        var listed = (await s.Admin.Products.ListAsync(categoryId: category.Id)).Data<PagedResult<ProductDto>>();
        Assert.Equal(product.Id, Assert.Single(listed.Items).Id);
        Assert.Empty((await s.Admin.Products.ListAsync(categoryId: Guid.NewGuid())).Data<PagedResult<ProductDto>>().Items); // no such category: nothing, as for an id that does not exist
    }

    [Fact]
    public async Task A_product_with_a_conversion_from_a_unit_that_does_not_exist_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);

        var response = await s.Admin.Products.CreateAsync(new ProductBuilder(s.Reference).WithConversion(Guid.NewGuid(), 6m).Build());

        Assert.Equal(Messages.ConversionInvalid, response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task The_lists_and_reports_filter_by_the_public_id_of_a_warehouse_and_an_unknown_id_filters_to_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 100m);
        var received = await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 3, 1m));
        var unknown = Guid.NewGuid();

        var documents = (await s.Admin.Documents.ListAsync(warehouseId: warehouse.Id)).Data<PagedResult<DocumentListItem>>();
        Assert.Equal(received.No, Assert.Single(documents.Items).No);
        Assert.Empty((await s.Admin.Documents.ListAsync(warehouseId: unknown)).Data<PagedResult<DocumentListItem>>().Items);

        var stock = (await s.Admin.Stock.QueryAsync(product.Id, warehouse.Id)).Data<PagedResult<StockRow>>();
        Assert.Equal(warehouse.Id, Assert.Single(stock.Items).WarehouseId);
        Assert.Empty((await s.Admin.Stock.QueryAsync(product.Id, unknown)).Data<PagedResult<StockRow>>().Items);

        Assert.Equal(3m, (await s.LedgerAsync(product.Id, warehouse.Id)).Sum(r => r.QtyDelta));
        Assert.Empty((await s.Admin.Stock.LedgerAsync(product.Id, unknown)).Data<PagedResult<LedgerRow>>().Items);

        var low = (await s.Admin.Stock.LowStockAsync(warehouse.Id)).Data<PagedResult<LowStockAlert>>();
        Assert.Equal(warehouse.Id, Assert.Single(low.Items, a => a.ProductId == product.Id).WarehouseId);
        Assert.Empty((await s.Admin.Stock.LowStockAsync(unknown)).Data<PagedResult<LowStockAlert>>().Items);

        Assert.Contains((await s.Admin.Reports.MovementsInWarehouseAsync(warehouse.Id)).Data<PagedResult<Ims.Application.Features.Reports.MovementRow>>().Items, r => r.DocNo == received.No);
        Assert.Empty((await s.Admin.Reports.MovementsInWarehouseAsync(unknown)).Data<PagedResult<Ims.Application.Features.Reports.MovementRow>>().Items);
        (await s.Admin.Reports.MovementsExportInWarehouseAsync(unknown)).ShouldBeOk();
    }

    [Fact]
    public async Task The_stock_list_filters_by_the_public_id_of_a_category()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var category = (await s.Admin.Categories.CreateAsync(Unique.Name("Cat"))).Data<CategoryDto>();
        var inCategory = await s.NewProductAsync(customize: b => b.WithCategory(category.Id));
        var other = await s.NewProductAsync();
        foreach (var product in new[] { inCategory, other }) await s.ReceiveAsync(s.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));

        var rows = (await s.Admin.GetAsync($"/api/stock?categoryId={category.Id}&pageSize={MaxWaitedPageSize}")).Data<PagedResult<StockRow>>();
        var none = (await s.Admin.GetAsync($"/api/stock?categoryId={Guid.NewGuid()}&pageSize={MaxWaitedPageSize}")).Data<PagedResult<StockRow>>();

        Assert.Equal(inCategory.Id, Assert.Single(rows.Items).ProductId);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Users_and_sessions_name_their_warehouses_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var accounts = new AccountFactory(fixture);

        var account = await accounts.CreateAsync(Roles.Clerk, warehouseIds: [warehouse.Id]);
        var (client, login) = await accounts.SignInAsync(account);

        var listed = (await s.Admin.GetAsync($"/api/users?search={account.Username}")).Data<PagedResult<UserDto>>().Items.Single(u => u.Id == account.Id);
        Assert.Equal([warehouse.Id], listed.WarehouseIds);
        Assert.Equal([warehouse.Id], login.User.WarehouseIds);
        Assert.Equal([warehouse.Id], (await client.Auth.MeAsync()).Data<UserInfo>().WarehouseIds);
        var moved = (await accounts.UpdateAsync(account, warehouseIds: [warehouse.Id, fixture.Reference.DefaultWarehouseId])).Data<UserDto>();
        Assert.Equivalent(new[] { warehouse.Id, fixture.Reference.DefaultWarehouseId }, moved.WarehouseIds);
    }

    [Fact]
    public async Task The_audit_log_names_these_entities_by_public_id_and_rows_written_before_the_flip_keep_their_integer()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var legacyId = Random.Shared.Next(100_000_000, 999_999_999).ToString();
        await fixture.Database.ExecuteAsync(
            "INSERT INTO AuditLogs (Actor, Action, EntityType, EntityId, [At]) VALUES (N'legacy', N'row.updated', N'Warehouse', @id, SYSUTCDATETIME())", ("@id", legacyId));

        var warehouse = await s.NewWarehouseAsync();
        await s.Admin.Warehouses.UpdateAsync(warehouse.Id, new WarehouseSaveRequest(warehouse.Code, "Renamed", null, null, true));

        var own = (await s.Admin.Audit.QueryAsync(entityId: warehouse.Id.ToString())).Data<AuditPage>().Items;
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowUpdated], own.Select(e => e.Action).Order());
        Assert.All(own, e => Assert.Equal((nameof(Warehouse), warehouse.Id.ToString()), (e.EntityType, e.EntityId)));
        var old = Assert.Single((await s.Admin.Audit.QueryAsync(entityType: nameof(Warehouse), entityId: legacyId)).Data<AuditPage>().Items);
        Assert.Equal(legacyId, old.EntityId); // the history mixes both ids; the page and its entity-id filter are plain text and show and match both
    }
}
