using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// The page statement of the documents list compiles to a plan that depends on <c>TOP(@p)</c>: compiled for a large page it scans every document line, and
/// the cached plan then serves page size 25 at 0.6 s and more (docs/dba/README.md section 4d). The page statement therefore asks for one plan for all values
/// (<c>DISABLE_PARAMETER_SNIFFING</c>). Plan quality cannot be asserted in a test without being flaky; what is asserted is that every shape of the page statement
/// (first page, filters, text search, cursor, offset page) carries the hint, that the count does not, and that the pages still hold the right rows. The requests go
/// through the real host, so the interceptor registration in <c>AddInfrastructure</c> is what is tested; the only addition is a log of the command text.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentsListPlanTests(ApiFixture fixture)
{
    private const string Hint = "OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))";
    private const string LineTable = "FROM [StockDocumentLine]";   // the page sums the lines of each document, the count does not touch them
    private const string CountCall = "COUNT(*)";

    [Fact]
    public async Task Every_shape_of_the_page_statement_carries_the_hint_and_the_count_does_not()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        await s.ReceiveAsync(warehouse, new Line(product.Id, 4, 2m));
        var newest = await s.ReceiveAsync(warehouse, new Line(product.Id, 3, 2m));

        var log = new CommandTextLog();
        using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var admin = ApiFixture.CreateClient(host);
        admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;

        var first = (await admin.GetAsync("/api/documents?pageSize=1")).Data<PagedResult<DocumentListItem>>();
        Assert.Equal(newest.Id, Assert.Single(first.Items).Id);
        AssertPageCarriesHint(log);
        Assert.NotEmpty(log.Containing(CountCall));
        Assert.All(log.Containing(CountCall), t => Assert.DoesNotContain("DISABLE_PARAMETER_SNIFFING", t, StringComparison.Ordinal));

        var shapes = new[]
        {
            $"/api/documents?pageSize=1&cursor={Uri.EscapeDataString(first.NextCursor!)}",   // Id < @olderThan
            "/api/documents?pageSize=1&page=2",                                              // OFFSET / FETCH
            $"/api/documents?pageSize=1&warehouseId={warehouse}&status={DocumentStatus.Posted}&type={DocumentType.Receipt}",
            "/api/documents?pageSize=1&search=" + Uri.EscapeDataString(newest.No),
            "/api/documents?pageSize=1&includeTotal=false",
        };
        foreach (var url in shapes)
        {
            log.Clear();
            var page = await admin.GetAsync(url);
            Assert.True(page.IsSuccess, url);
            AssertPageCarriesHint(log);
        }
    }

    [Fact]
    public async Task The_list_returns_the_same_rows_for_every_page_size_whichever_size_comes_first()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        for (var i = 0; i < 3; i++) await s.ReceiveAsync(warehouse, new Line(product.Id, 1 + i, 2m));

        var large = (await s.Admin.Documents.ListAsync(warehouseId: warehouse, pageSize: 200)).Data<PagedResult<DocumentListItem>>();
        var small = (await s.Admin.Documents.ListAsync(warehouseId: warehouse, pageSize: 2)).Data<PagedResult<DocumentListItem>>();

        Assert.Equal(large.Items.Take(2).Select(d => d.Id), small.Items.Select(d => d.Id));
        Assert.Equal(large.Total, small.Total);
        Assert.True(large.Items.Select(d => d.Id).SequenceEqual(large.Items.Select(d => d.Id).OrderDescending()));
    }

    private static void AssertPageCarriesHint(CommandTextLog log)
    {
        var page = Assert.Single(log.Containing(LineTable));
        Assert.EndsWith(Hint, page, StringComparison.Ordinal);
        Assert.DoesNotContain("RECOMPILE", page, StringComparison.Ordinal);
    }
}
