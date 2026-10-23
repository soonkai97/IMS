using Ims.Api.Tests.Harness;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// The concurrency token of an order is its header's ROW_VERSION. A save that replaces only the lines changes no column of the header, so the context must mark the header as revised
/// (<c>TouchSalesOrdersWithChangedLines</c>), or the token a client holds would stay valid across an edit of the lines. Tested on the DbContext itself, not through the API, which also
/// replaces the header's own fields and so could not tell.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderRowVersionTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<byte[]> RowVersionAsync(Guid publicId) =>
        fixture.Database.ScalarAsync<byte[]>("SELECT ROW_VERSION FROM SalesOrder WHERE PublicId = @id", ("@id", publicId));

    [Fact]
    public async Task Replacing_only_the_lines_of_an_order_moves_the_row_version_of_its_header()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var order = await _s.CreateAsync(warehouse.Id, product.Id);
        var before = await RowVersionAsync(order.Id);

        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.Set<SalesOrder>().Include(o => o.Lines).SingleAsync(o => o.PublicId == order.Id);
            var item = await db.Set<Product>().Include(p => p.Conversions).SingleAsync(p => p.PublicId == product.Id);
            tracked.ReplaceLines([SalesOrderLine.Create(item, 7m, null, 3m, null)]); // the header's own columns are not touched
            Assert.Equal(EntityState.Unchanged, db.Entry(tracked).State);

            await db.SaveChangesAsync();
        }

        var after = await RowVersionAsync(order.Id);
        Assert.NotEqual(before, after);
        Assert.Equal(21m, (await _s.Admin.SalesOrders.GetAsync(order.Id)).Data<Ims.Application.Features.Sales.SalesOrderDto>().TotalAmount);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_leaves_the_row_version_alone()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var order = await _s.CreateAsync(warehouse.Id, (await _s.NewProductAsync()).Id);
        var before = await RowVersionAsync(order.Id);

        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<SalesOrder>().Include(o => o.Lines).SingleAsync(o => o.PublicId == order.Id);
            await db.SaveChangesAsync();
        }

        Assert.Equal(before, await RowVersionAsync(order.Id));
    }
}
