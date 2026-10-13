using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Import;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan PR-6: products are addressed by their public id everywhere the API names them: their own routes and responses, document lines, stock rows, the low-stock list,
/// the shortage problems and every product filter of the stock list, the ledger, the movements report and the exports. The integer id stays inside; imports still find a product by SKU.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class ProductFlipTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>The look-up an <c>IIdMap</c> makes: nothing but the integer and the public id of rows of the product table.</summary>
    [GeneratedRegex(@"^SELECT \[\w+\]\.\[Id\], \[\w+\]\.\[PublicId\]\s+FROM \[Product\]", RegexOptions.IgnoreCase)]
    private static partial Regex ProductByPublicId();

    [Fact]
    public async Task A_created_edited_listed_and_looked_up_product_answers_with_the_public_id_stored_for_it()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithBarcodes(Unique.Sku("BC")));
        var stored = await fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM Product WHERE Sku = @v", ("@v", product.Sku));

        var edited = (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(s.Reference, product).WithName("Renamed").Build())).Data<ProductDto>();
        var read = await s.Admin.Products.GetAsync(product.Id);
        var listed = (await s.Admin.Products.ListAsync(product.Sku)).Data<PagedResult<ProductDto>>().Items;

        Assert.Equal(stored, product.Id);
        Assert.Equal(stored, edited.Id);
        Assert.Equal(stored, read.Data<ProductDto>().Id);
        Assert.Equal(stored, Assert.Single(listed).Id);
        Assert.Equal(stored, (await s.Admin.Products.FindAsync(product.Sku)).Id);
        Assert.Equal(stored, (await s.Admin.Products.FindAsync(product.Barcodes.Single())).Id);
        Assert.Equal(JsonValueKind.String, read.Json().RootElement.GetProperty("id").ValueKind); // a string on the wire, never a number
    }

    [Fact]
    public async Task The_integer_route_of_a_product_is_gone_and_an_unknown_or_foreign_public_id_is_404_with_one_body()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var body = new ProductBuilder(s.Reference).Build();
        var wrongType = s.Reference.DefaultWarehouseId; // a real public id, of a warehouse

        (await s.Admin.GetAsync("/api/products/1")).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.PutAsync("/api/products/1", body)).ShouldBe(HttpStatusCode.NotFound);
        var unknown = (await s.Admin.Products.GetAsync(Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.NotFound);
        var foreign = (await s.Admin.Products.GetAsync(wrongType)).ShouldBeProblem(HttpStatusCode.NotFound);
        var empty = (await s.Admin.Products.GetAsync(Guid.Empty)).ShouldBeProblem(HttpStatusCode.NotFound);
        var put = (await s.Admin.Products.UpdateAsync(wrongType, body)).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.All(new[] { foreign, empty, put }, p => Assert.Equal((unknown.Title, unknown.Detail), (p.Title, p.Detail)));
    }

    [Fact]
    public async Task A_document_line_names_its_product_by_public_id_and_a_product_that_does_not_exist_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 8, 1m));

        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 3));
        var line = Assert.Single((await s.Admin.Documents.GetAsync(draft.Id)).Data<DocumentDto>().Lines);
        Assert.Equal((product.Id, 8m), (line.ProductId, line.Available)); // the stock was found through the integer key the DTO keeps to itself

        foreach (var missing in new[] { Guid.NewGuid(), Warehouse }) // unknown, and the public id of something that is not a product
        {
            var response = await s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(Warehouse).WithLines(new Line(missing, 1)).Build());
            Assert.Equal(Messages.ProductMissing(missing), response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        }
        var empty = await s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(Warehouse).WithLines(new Line(Guid.Empty, 1)).Build());
        Assert.Contains(Messages.ChooseProduct, empty.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Values.SelectMany(v => v));
    }

    [Fact]
    public async Task The_stock_ledger_movement_and_export_filters_take_the_public_id_and_an_unknown_or_foreign_id_filters_to_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var category = (await s.Admin.Categories.CreateAsync(Unique.Name("Cat"))).Data<CategoryDto>();
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync(safetyStock: 100m, customize: b => b.WithCategory(category.Id));
        var other = await s.NewProductAsync(customize: b => b.WithCategory(category.Id));
        foreach (var p in new[] { product, other }) await s.ReceiveAsync(warehouse.Id, new Line(p.Id, 4, 1m));

        var stock = (await s.Admin.Stock.QueryAsync(product.Id, warehouse.Id)).Data<PagedResult<StockRow>>();
        Assert.Equal(product.Id, Assert.Single(stock.Items).ProductId);
        // product, warehouse and category together: each is its own integer inside, so a swap of two of them would show here
        var all = (await s.Admin.GetAsync($"/api/stock?productId={product.Id}&warehouseId={warehouse.Id}&categoryId={category.Id}")).Data<PagedResult<StockRow>>();
        Assert.Equal(product.Id, Assert.Single(all.Items).ProductId);
        Assert.Empty((await s.Admin.GetAsync($"/api/stock?productId={product.Id}&warehouseId={warehouse.Id}&categoryId={Guid.NewGuid()}")).Data<PagedResult<StockRow>>().Items);
        var lowOnly = (await s.Admin.GetAsync($"/api/stock?productId={product.Id}&lowOnly=true")).Data<PagedResult<StockRow>>();
        Assert.Equal(product.Id, Assert.Single(lowOnly.Items).ProductId);
        Assert.Equal(product.Id, Assert.Single((await s.Admin.Stock.LowStockAsync(warehouse.Id)).Data<PagedResult<LowStockAlert>>().Items, a => a.ProductId == product.Id).ProductId);

        Assert.Equal(4m, Assert.Single(await s.LedgerAsync(product.Id, warehouse.Id)).QtyDelta);
        Assert.Equal(product.Sku, Assert.Single((await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>().Items).Sku);

        foreach (var missing in new[] { Guid.NewGuid(), s.Reference.DefaultWarehouseId, Guid.Empty })
        {
            Assert.Empty((await s.Admin.Stock.QueryAsync(missing)).Data<PagedResult<StockRow>>().Items);
            Assert.Empty((await s.Admin.GetAsync($"/api/stock?productId={missing}&lowOnly=true")).Data<PagedResult<StockRow>>().Items);
            Assert.Empty((await s.Admin.Stock.LedgerAsync(missing)).Data<PagedResult<LedgerRow>>().Items);
            Assert.Empty((await s.Admin.Reports.MovementsAsync(missing)).Data<PagedResult<MovementRow>>().Items);
            foreach (var export in new[] { s.Admin.Reports.StockSummaryExportAsync(missing), s.Admin.Reports.MovementsExportAsync(missing), s.Admin.Reports.LedgerExportAsync(missing) })
                Assert.Empty(Xlsx.Read((await export).Bytes).Rows);
        }
    }

    [Fact]
    public async Task A_product_filter_is_translated_with_one_look_up_and_the_rows_carry_their_public_id_without_any()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var products = new List<ProductDto>();
        for (var i = 0; i < 3; i++) products.Add(await s.NewProductAsync());
        await s.ReceiveAsync(Warehouse, products.Select(p => new Line(p.Id, 2, 1m)).ToArray());
        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, products.Select(p => new Line(p.Id, 1)).ToArray());
        var log = new Documents.DocumentReadQueryTests.CommandLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<Ims.Infrastructure.Persistence.AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<Ims.Application.Features.Auth.AuthResult>().AccessToken;

        async Task<int> LookupsAsync(Func<Task<ApiResponse>> request)
        {
            log.Commands.Clear();
            (await request()).ShouldBeOk();
            return log.Commands.Count(c => ProductByPublicId().IsMatch(c));
        }

        Assert.Equal(1, await LookupsAsync(() => client.Stock.QueryAsync(products[0].Id)));
        Assert.Equal(1, await LookupsAsync(() => client.Stock.LedgerAsync(products[1].Id)));
        Assert.Equal(1, await LookupsAsync(() => client.Reports.MovementsAsync(products[2].Id)));
        Assert.Equal(0, await LookupsAsync(() => client.Stock.QueryAsync()));                  // the rows project the public id from the product they join already
        Assert.Equal(0, await LookupsAsync(() => client.Stock.LowStockAsync()));
        Assert.Equal(0, await LookupsAsync(() => client.Documents.GetAsync(draft.Id)));        // nor does a document with three lines
    }

    [Fact]
    public async Task The_shortage_problems_name_the_product_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 5));

        var post = await s.Admin.Documents.SubmitAsync(draft.Id); // the stock check happens at submit

        var body = post.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(product.Id, body.ProductId);
        Assert.Contains(product.Sku, body.Detail); // still named in words: the integer ids never reach the message
        Assert.DoesNotMatch(@"product \d+", body.Detail ?? "");
    }

    [Fact]
    public async Task An_opening_stock_import_finds_products_by_sku_and_the_draft_names_them_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var file = Xlsx.Create("Opening stock", ["SKU", "Warehouse", "Qty", "UnitCost"], [product.Sku, fixture.Reference.DefaultWarehouseCode, 6, 2]);

        var result = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).Data<ImportResult>();

        var draft = (await s.Admin.Documents.GetAsync(result.DocumentId!.Value)).Data<DocumentDto>();
        Assert.Equal((product.Id, 6m), (Assert.Single(draft.Lines).ProductId, draft.Lines[0].Qty));
    }

    [Fact]
    public async Task The_audit_log_names_a_product_by_public_id_and_rows_written_before_the_flip_keep_their_integer()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var legacyId = Random.Shared.Next(100_000_000, 999_999_999).ToString();
        await fixture.Database.ExecuteAsync(
            "INSERT INTO AuditLogs (Actor, Action, EntityType, EntityId, [At]) VALUES (N'legacy', N'row.updated', N'Product', @id, SYSUTCDATETIME())", ("@id", legacyId));

        var product = await s.NewProductAsync();
        (await s.Admin.Products.UpdateAsync(product.Id, ProductBuilder.From(s.Reference, product).WithName("Renamed").Build())).ShouldBeOk();

        var own = (await s.Admin.Audit.QueryAsync(entityId: product.Id.ToString())).Data<AuditPage>().Items;
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowUpdated], own.Select(e => e.Action).Order());
        Assert.All(own, e => Assert.Equal((nameof(Product), product.Id.ToString()), (e.EntityType, e.EntityId)));
        Assert.Equal(legacyId, Assert.Single((await s.Admin.Audit.QueryAsync(entityType: nameof(Product), entityId: legacyId)).Data<AuditPage>().Items).EntityId);
    }
}
