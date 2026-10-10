using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Import;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Import;

/// <summary>D7: opening stock arrives as draft receipts for review; nothing touches stock until someone posts them.</summary>
[Collection(ApiCollection.Name)]
public class OpeningStockImportTests(ApiFixture fixture)
{
    private static readonly string[] Headers = ["SKU", "Warehouse", "Qty", "UnitCost"];

    private string DefaultWarehouse => fixture.Reference.DefaultWarehouseCode;

    [Fact]
    public async Task Preview_checks_every_row_and_creates_no_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var documentsBefore = (await s.Admin.Documents.ListAsync(DocumentType.Receipt, pageSize: 1)).Data<PagedResult<DocumentListItem>>().Total;
        var file = Xlsx.Create("Opening stock", Headers,
            [product.Sku, DefaultWarehouse, 10, 2.5],                // fine
            ["NO-SUCH-SKU", DefaultWarehouse, 1, 1],                 // unknown product
            [product.Sku, "NO-SUCH-WH", 1, 1],                       // unknown warehouse
            [product.Sku, DefaultWarehouse, 0, 1],                   // zero quantity
            [product.Sku, DefaultWarehouse, 1, -1]);                 // negative cost

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();

        Assert.False(result.Committed);
        Assert.Equal((5, 1, 4, 0), (result.Total, result.Valid, result.Invalid, result.Created));
        Assert.Null(result.DocumentId);
        Assert.Contains("Unknown or inactive SKU", Assert.Single(result.Rows[1].Errors));
        Assert.Contains("Unknown or inactive warehouse", Assert.Single(result.Rows[2].Errors));
        Assert.Contains("greater than zero", Assert.Single(result.Rows[3].Errors));
        Assert.Contains("cost", Assert.Single(result.Rows[4].Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(documentsBefore, (await s.Admin.Documents.ListAsync(DocumentType.Receipt, pageSize: 1)).Data<PagedResult<DocumentListItem>>().Total);
    }

    [Fact]
    public async Task Commit_creates_a_draft_receipt_that_changes_no_stock_until_it_is_posted()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", Headers,
            [a.Sku, DefaultWarehouse, 10, 2.5],
            [b.Sku, DefaultWarehouse, 4, 8]);

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).Data<ImportResult>();

        Assert.True(result.Committed);
        Assert.Equal((2, 2, 0, 2), (result.Total, result.Valid, result.Invalid, result.Created));
        var draft = await s.GetAsync(result.DocumentId!.Value);
        Assert.Equal((DocumentType.Receipt, DocumentStatus.Draft, "Opening stock (import)"), (draft.Type, draft.Status, draft.Remark));
        Assert.Equal(fixture.Reference.DefaultWarehouseId, draft.WarehouseId);
        Assert.Equal(new[] { (a.Sku, 10m, 2.5m), (b.Sku, 4m, 8m) }.Order(), draft.Lines.Select(l => (l.Sku, l.BaseQty, l.UnitCost)).Order());
        Assert.Equal(0, await s.OnHandAsync(a.Id, fixture.Reference.DefaultWarehouseId));

        (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeOk();
        (await s.Admin.Documents.PostAsync(draft.Id)).ShouldBeOk();

        var balance = (await s.BalanceAsync(a.Id, fixture.Reference.DefaultWarehouseId))!;
        Assert.Equal((10m, 2.5m), (balance.QtyOnHand, balance.AvgCost));
        Assert.Equal(4, await s.OnHandAsync(b.Id, fixture.Reference.DefaultWarehouseId));
    }

    [Fact]
    public async Task Commit_with_any_invalid_row_is_refused_and_no_document_is_created()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var before = (await s.Admin.Documents.ListAsync(DocumentType.Receipt, pageSize: 1)).Data<PagedResult<DocumentListItem>>().Total;
        var file = Xlsx.Create("Opening stock", Headers, [product.Sku, DefaultWarehouse, 10, 2], ["NO-SUCH-SKU", DefaultWarehouse, 1, 1]);

        var response = await s.Admin.Import.OpeningStockAsync(file, commit: true);

        Assert.Contains("nothing was imported", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(before, (await s.Admin.Documents.ListAsync(DocumentType.Receipt, pageSize: 1)).Data<PagedResult<DocumentListItem>>().Total);
    }

    [Fact]
    public async Task Rows_for_different_warehouses_become_one_draft_receipt_each()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", Headers,
            [product.Sku, DefaultWarehouse, 3, 1],
            [product.Sku, other.Code, 7, 1]);

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal(2, result.Created);
        var inOther = (await s.Admin.Documents.ListAsync(DocumentType.Receipt, DocumentStatus.Draft, other.Id)).Data<PagedResult<DocumentListItem>>();
        var created = Assert.Single(inOther.Items);
        Assert.Equal(7, created.TotalQty);
    }

    [Fact]
    public async Task A_warehouse_the_importer_cannot_access_is_a_row_error()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", Headers, [product.Sku, other.Code, 5, 1], [product.Sku, DefaultWarehouse, 5, 1]);

        var bySupervisor = (await s.Supervisor.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();
        var byAdmin = (await s.Admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();

        Assert.Contains("do not have access", Assert.Single(bySupervisor.Rows[0].Errors));
        Assert.True(bySupervisor.Rows[1].Ok);
        Assert.Equal(2, byAdmin.Valid);
    }

    [Fact]
    public async Task An_inactive_product_is_not_accepted()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.Inactive());
        var file = Xlsx.Create("Opening stock", Headers, [product.Sku, DefaultWarehouse, 5, 1]);

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: false)).Data<ImportResult>();

        Assert.Equal((0, 1), (result.Valid, result.Invalid));
        Assert.Contains("inactive", Assert.Single(Assert.Single(result.Rows).Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_file_without_the_required_columns_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var file = Xlsx.Create("Opening stock", ["SKU", "Qty"], ["X", 1]);

        var problem = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("warehouse", problem.Detail);
    }

    [Fact]
    public async Task A_missing_unit_cost_means_zero()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", Headers, [product.Sku, DefaultWarehouse, 5, null]);

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).Data<ImportResult>();

        Assert.Equal(0m, Assert.Single((await s.GetAsync(result.DocumentId!.Value)).Lines).UnitCost);
    }
}
