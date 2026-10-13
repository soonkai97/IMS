using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Warehouses;

namespace Ims.Api.Tests.Tests.MasterData;

[Collection(ApiCollection.Name)]
public class WarehouseTests(ApiFixture fixture)
{
    private static WarehouseSaveRequest Request(string? code = null, bool isActive = true) =>
        new(code ?? Unique.Code("w"), Unique.Name("Warehouse"), "Contact person", "Some notes", isActive);

    [Fact]
    public async Task A_warehouse_code_is_stored_in_upper_case()
    {
        var admin = await fixture.AsAsync("Admin");
        var request = Request();

        var created = (await admin.Warehouses.CreateAsync(request)).Data<WarehouseDto>();

        Assert.Equal(request.Code.ToUpperInvariant(), created.Code);
        Assert.Equal("Contact person", created.Contact);
        Assert.True(created.IsActive);
        Assert.Contains((await admin.Warehouses.ListAsync()).Data<List<WarehouseDto>>(), w => w.Id == created.Id);
    }

    [Fact]
    public async Task A_duplicate_code_is_409_on_create_and_on_update()
    {
        var admin = await fixture.AsAsync("Admin");
        var first = (await admin.Warehouses.CreateAsync(Request())).Data<WarehouseDto>();
        var second = (await admin.Warehouses.CreateAsync(Request())).Data<WarehouseDto>();

        (await admin.Warehouses.CreateAsync(Request(first.Code.ToLowerInvariant()))).ShouldBeProblem(HttpStatusCode.Conflict);
        (await admin.Warehouses.UpdateAsync(second.Id, Request(first.Code))).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_warehouse_can_be_updated_keeping_its_own_code()
    {
        var admin = await fixture.AsAsync("Admin");
        var created = (await admin.Warehouses.CreateAsync(Request())).Data<WarehouseDto>();

        var updated = (await admin.Warehouses.UpdateAsync(created.Id, Request(created.Code) with { Name = "New name" })).Data<WarehouseDto>();

        Assert.Equal("New name", updated.Name);
        Assert.Equal(created.Code, updated.Code);
    }

    [Fact]
    public async Task Deactivating_hides_the_warehouse_by_default_and_blocks_new_documents()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(warehouse.Id).WithLines(new Line(product.Id, 1, 1m)).Build();
        (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeOk();

        (await s.Admin.Warehouses.UpdateAsync(warehouse.Id, Request(warehouse.Code, isActive: false))).ShouldBeOk();

        Assert.DoesNotContain((await s.Admin.Warehouses.ListAsync()).Data<List<WarehouseDto>>(), w => w.Id == warehouse.Id);
        Assert.Contains((await s.Admin.Warehouses.ListAsync(includeInactive: true)).Data<List<WarehouseDto>>(), w => w.Id == warehouse.Id && !w.IsActive);
        var refused = await s.Admin.Documents.CreateReceiptAsync(request);
        Assert.Contains("inactive", refused.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);

        (await s.Admin.Warehouses.UpdateAsync(warehouse.Id, Request(warehouse.Code, isActive: true))).ShouldBeOk();
        (await s.Admin.Documents.CreateReceiptAsync(request)).ShouldBeOk();
    }

    [Theory]
    [InlineData("", "Name", "Code")]
    [InlineData("OKCODE", "", "Name")]
    [InlineData("THISCODEISWAYTOOLONGFORTHEFIELD", "Name", "Code")]
    public async Task Invalid_input_is_400_with_a_field_error(string code, string name, string field)
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.Warehouses.CreateAsync(new WarehouseSaveRequest(code, name, null, null, true));

        Assert.NotEmpty(response.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor(field));
    }

    [Fact]
    public async Task A_missing_warehouse_is_404()
    {
        var admin = await fixture.AsAsync("Admin");

        (await admin.Warehouses.UpdateAsync(Guid.NewGuid(), Request())).ShouldBeProblem(HttpStatusCode.NotFound);
    }
}
