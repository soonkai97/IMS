using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// DDD-13: the lookup the price-list audit subscriber uses to name the products and the units of a save in words. It must answer for the whole save in ONE statement (never one per item), return what exists and
/// leave out what does not, and send nothing when nothing is asked for.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListLabelReaderTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private async Task<(PriceListLabels Labels, int Statements)> ReadAsync(IReadOnlyCollection<int> products, IReadOnlyCollection<int> uoms)
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        using var scope = host.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPriceListLabelReader>();
        log.Clear();
        return (await reader.ReadAsync(products, uoms, CancellationToken.None), log.Count);
    }

    private Task<int> ProductKeyAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM Product WHERE PublicId = @id", ("@id", publicId));

    private Task<int> UomKeyAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM Uom WHERE PublicId = @id", ("@id", publicId));

    [Fact]
    public async Task The_sku_of_every_product_and_the_code_of_every_unit_asked_for_come_back_in_one_statement()
    {
        var (a, b) = (await _s.NewProductAsync(), await _s.NewProductAsync());
        int[] products = [await ProductKeyAsync(a.Id), await ProductKeyAsync(b.Id)];
        int[] uoms = [await UomKeyAsync(fixture.Reference.PieceUnitId), await UomKeyAsync(fixture.Reference.CartonUnitId)];

        var (labels, statements) = await ReadAsync(products, uoms);

        Assert.Equal(1, statements);
        Assert.Equal([a.Sku, b.Sku], products.Select(k => labels.Skus[k]));
        Assert.Equal(2, labels.Skus.Count);
        Assert.Equal(["PCS", "CTN"], uoms.Select(k => labels.UomCodes[k]));
        Assert.Equal(2, labels.UomCodes.Count);
    }

    [Fact]
    public async Task A_key_that_has_no_row_is_left_out_and_a_key_asked_for_twice_is_answered_once()
    {
        var a = await _s.NewProductAsync();
        var key = await ProductKeyAsync(a.Id);

        var (labels, statements) = await ReadAsync([key, key, int.MaxValue], [int.MaxValue]);

        Assert.Equal(1, statements);
        Assert.Equal(a.Sku, Assert.Single(labels.Skus).Value);
        Assert.Empty(labels.UomCodes);
    }

    [Fact]
    public async Task Only_products_or_only_units_are_one_statement_and_nothing_asked_for_is_none()
    {
        var a = await _s.NewProductAsync();
        var key = await ProductKeyAsync(a.Id);

        var onlyProducts = await ReadAsync([key], []);
        var onlyUnits = await ReadAsync([], [await UomKeyAsync(fixture.Reference.PieceUnitId)]);
        var nothing = await ReadAsync([], []);

        Assert.Equal((1, 1, 0), (onlyProducts.Statements, onlyUnits.Statements, nothing.Statements));
        Assert.Equal(a.Sku, onlyProducts.Labels.Skus[key]);
        Assert.Single(onlyUnits.Labels.UomCodes);
        Assert.Empty(nothing.Labels.Skus);
    }
}
