using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>AUTH-02 data scope: a user assigned to the default warehouse must not see or touch another warehouse.</summary>
[Collection(ApiCollection.Name)]
public class WarehouseScopeTests(ApiFixture fixture)
{
    private sealed record Setup(StockScenario Scenario, WarehouseDto OtherWarehouse, ProductDto Product, DocumentDto DocumentInOther);

    /// <summary>Every row of the low-stock list (all pages, 500 at a time).</summary>
    private static async Task<List<LowStockAlert>> AllLowStockAsync(Clients.ApiClient who, int? warehouseId)
    {
        var rows = new List<LowStockAlert>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/low-stock?page={page}&pageSize=500" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"))).Data<PagedResult<LowStockAlert>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    /// <summary>A second warehouse holding a posted receipt of a low-stock product, plus a draft receipt, all outside the staff user's scope.</summary>
    private async Task<Setup> CreateOtherWarehouseAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 100);
        await s.ReceiveAsync(other.Id, new Line(product.Id, 5, 2m));
        var draft = await s.DraftReceiptAsync(s.Admin, other.Id, new Line(product.Id, 1, 2m));
        return new Setup(s, other, product, draft);
    }

    [Fact]
    public async Task A_warehouse_the_user_is_not_assigned_to_is_missing_from_the_warehouse_list()
    {
        var setup = await CreateOtherWarehouseAsync();

        var staffView = (await setup.Scenario.Staff.Warehouses.ListAsync(includeInactive: true)).Data<List<WarehouseDto>>();
        var adminView = (await setup.Scenario.Admin.Warehouses.ListAsync()).Data<List<WarehouseDto>>();

        Assert.DoesNotContain(staffView, w => w.Id == setup.OtherWarehouse.Id);
        Assert.Contains(staffView, w => w.Id == fixture.Reference.DefaultWarehouseId);
        Assert.Contains(adminView, w => w.Id == setup.OtherWarehouse.Id);
    }

    [Fact]
    public async Task Documents_of_another_warehouse_are_invisible_in_lists_even_when_filtered_for_it()
    {
        var setup = await CreateOtherWarehouseAsync();
        var staff = setup.Scenario.Staff;

        var all = (await staff.Documents.ListAsync(pageSize: 200)).Data<PagedResult<DocumentListItem>>();
        var filtered = (await staff.Documents.ListAsync(warehouseId: setup.OtherWarehouse.Id)).Data<PagedResult<DocumentListItem>>();
        var bySearch = (await staff.Documents.ListAsync(search: setup.DocumentInOther.No)).Data<PagedResult<DocumentListItem>>();

        Assert.DoesNotContain(all.Items, d => d.No == setup.DocumentInOther.No);
        Assert.Empty(filtered.Items);
        Assert.Empty(bySearch.Items);
        Assert.Contains((await setup.Scenario.Admin.Documents.ListAsync(search: setup.DocumentInOther.No)).Data<PagedResult<DocumentListItem>>().Items,
            d => d.No == setup.DocumentInOther.No);
    }

    [Fact]
    public async Task Direct_access_to_a_document_of_another_warehouse_is_404_so_ids_cannot_be_probed()
    {
        var setup = await CreateOtherWarehouseAsync();
        var staff = setup.Scenario.Staff;
        var id = setup.DocumentInOther.Id;

        (await staff.Documents.GetAsync(id)).ShouldBeProblem(HttpStatusCode.NotFound, "Not found");
        (await staff.Documents.SubmitAsync(id)).ShouldBe(HttpStatusCode.NotFound);
        (await staff.Documents.CancelAsync(id)).ShouldBe(HttpStatusCode.NotFound);
        (await staff.Documents.PostAsync(id)).ShouldBe(HttpStatusCode.NotFound);
        var edit = new DocumentBuilder(setup.OtherWarehouse.Id).WithLines(new Line(setup.Product.Id, 2, 1m)).Build();
        (await staff.Documents.UpdateAsync(id, edit)).ShouldBe(HttpStatusCode.NotFound);

        // Nothing changed behind the 404s.
        Assert.Equal(DocumentStatus.Draft, (await setup.Scenario.GetAsync(id)).Status);
    }

    [Fact]
    public async Task Creating_a_document_in_another_warehouse_is_403()
    {
        var setup = await CreateOtherWarehouseAsync();
        var request = new DocumentBuilder(setup.OtherWarehouse.Id).WithLines(new Line(setup.Product.Id, 1, 1m)).Build();

        (await setup.Scenario.Staff.Documents.CreateReceiptAsync(request)).ShouldBe(HttpStatusCode.Forbidden);
        (await setup.Scenario.Clerk.Documents.CreateIssueAsync(request)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stock_ledger_low_stock_and_reports_of_another_warehouse_are_hidden()
    {
        var setup = await CreateOtherWarehouseAsync();
        var s = setup.Scenario;
        var productId = setup.Product.Id;

        Assert.Empty((await s.Staff.Stock.QueryAsync(productId: productId)).Data<PagedResult<StockRow>>().Items);
        Assert.Empty((await s.Staff.Stock.LedgerAsync(productId: productId)).Data<PagedResult<LedgerRow>>().Items);
        // Every page of the lists is read: a leak on page 2 is a leak (the first page alone proves nothing on a list this long).
        Assert.DoesNotContain(await AllLowStockAsync(s.Staff, setup.OtherWarehouse.Id), a => a.ProductId == productId);
        Assert.DoesNotContain(await AllLowStockAsync(s.Staff, null), a => a.WarehouseId == setup.OtherWarehouse.Id);
        Assert.Empty((await s.Supervisor.Reports.MovementsAsync(productId)).Data<PagedResult<MovementRow>>().Items);

        // The same questions asked by someone who sees every warehouse.
        Assert.Single((await s.Admin.Stock.QueryAsync(productId: productId)).Data<PagedResult<StockRow>>().Items);
        Assert.Single((await s.Admin.Stock.LedgerAsync(productId: productId)).Data<PagedResult<LedgerRow>>().Items);
        Assert.Contains(await AllLowStockAsync(s.Admin, setup.OtherWarehouse.Id), a => a.ProductId == productId);
        Assert.Single((await s.Owner.Reports.MovementsAsync(productId)).Data<PagedResult<MovementRow>>().Items);
    }

    [Fact]
    public async Task Assigning_the_warehouse_to_the_user_grants_access_after_the_next_sign_in()
    {
        var setup = await CreateOtherWarehouseAsync();
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.WarehouseStaff, warehouseIds: [fixture.Reference.DefaultWarehouseId]);
        var (before, _) = await accounts.SignInAsync(account);
        (await before.Documents.GetAsync(setup.DocumentInOther.Id)).ShouldBe(HttpStatusCode.NotFound);

        (await accounts.UpdateAsync(account, warehouseIds: [fixture.Reference.DefaultWarehouseId, setup.OtherWarehouse.Id])).ShouldBeOk();
        var (after, _) = await accounts.SignInAsync(account);

        Assert.Equal(setup.DocumentInOther.No, (await after.Documents.GetAsync(setup.DocumentInOther.Id)).Data<DocumentDto>().No);
        Assert.Contains((await after.Warehouses.ListAsync()).Data<List<WarehouseDto>>(), w => w.Id == setup.OtherWarehouse.Id);
    }

    [Fact]
    public async Task A_user_assigned_only_to_the_other_warehouse_cannot_see_the_default_one()
    {
        var setup = await CreateOtherWarehouseAsync();
        var accounts = new AccountFactory(fixture);
        var account = await accounts.CreateAsync(Roles.Clerk, warehouseIds: [setup.OtherWarehouse.Id]);
        var (clerk, _) = await accounts.SignInAsync(account);
        var inDefault = await setup.Scenario.DraftReceiptAsync(setup.Scenario.Admin, fixture.Reference.DefaultWarehouseId, new Line(setup.Product.Id, 1, 1m));

        (await clerk.Documents.GetAsync(inDefault.Id)).ShouldBe(HttpStatusCode.NotFound);
        var visible = (await clerk.Warehouses.ListAsync()).Data<List<WarehouseDto>>();
        Assert.Equal([setup.OtherWarehouse.Id], visible.Select(w => w.Id));
    }

    [Fact]
    public async Task A_scoped_supervisor_cannot_change_a_warehouse_outside_their_scope()
    {
        var setup = await CreateOtherWarehouseAsync();
        var supervisor = setup.Scenario.Supervisor;
        var request = new WarehouseSaveRequest(setup.OtherWarehouse.Code, "Renamed by an outsider", null, null, false);

        (await supervisor.Warehouses.UpdateAsync(setup.OtherWarehouse.Id, request)).ShouldBe(HttpStatusCode.Forbidden);

        var unchanged = (await setup.Scenario.Admin.Warehouses.ListAsync()).Data<List<WarehouseDto>>().Single(w => w.Id == setup.OtherWarehouse.Id);
        Assert.Equal((setup.OtherWarehouse.Name, true), (unchanged.Name, unchanged.IsActive));
        (await setup.Scenario.Admin.Warehouses.UpdateAsync(setup.OtherWarehouse.Id, request)).ShouldBeOk();
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public async Task Admin_and_owner_see_every_warehouse(string role)
    {
        var setup = await CreateOtherWarehouseAsync();
        var client = setup.Scenario.As(role);

        Assert.Contains((await client.Warehouses.ListAsync()).Data<List<WarehouseDto>>(), w => w.Id == setup.OtherWarehouse.Id);
        (await client.Documents.GetAsync(setup.DocumentInOther.Id)).ShouldBeOk();
    }
}
