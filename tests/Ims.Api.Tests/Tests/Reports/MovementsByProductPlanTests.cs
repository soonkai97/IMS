using System.Data.Common;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Reports;
using Ims.Infrastructure.Persistence;
using Ims.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>
/// The movements report for one product asks SQL Server for a plan of its own on every call. Without it the cached plan of the first product serves
/// all products, and the plan that suits a product with thousands of lines reads every document when the product has none (2.4 million reads at
/// 450,000 lines, docs/dba/README.md section 4b). Plan quality cannot be asserted in a test without being flaky; what is asserted is that the page
/// and both counts carry the hint when a product is filtered, that other filters do not, and that a product with no movements gets an empty report.
/// </summary>
[Collection(ApiCollection.Name)]
public class MovementsByProductPlanTests(ApiFixture fixture)
{
    private const string Hint = "OPTION (RECOMPILE)";

    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Texts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>A reader on the test database whose interceptors are the production one, then a log of what it finally sent.</summary>
    private (EfMovementReader Reader, AppDbContext Db, CommandLog Log) NewReader()
    {
        var log = new CommandLog();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.Database.ConnectionString).AddInterceptors(new RecompileInterceptor(), log).Options;
        var db = new AppDbContext(options, new SystemClock());
        return (new EfMovementReader(db), db, log);
    }

    [Fact]
    public async Task A_product_filter_marks_the_page_and_both_counts_for_recompile_and_other_filters_do_not()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 4, 2m));

        var (reader, db, log) = NewReader();
        await using (db)
        {
            var byProduct = new MovementFilter(null, null, product.Id, null, null);
            Assert.Equal(1, await reader.CountAsync(byProduct, null, default));
            var page = await reader.PageAsync(byProduct, null, default);
            Assert.Equal(product.Sku, Assert.Single(page).Sku);
            Assert.Equal(3, log.Texts.Count);
            Assert.All(log.Texts, t => Assert.EndsWith(Hint, t, StringComparison.Ordinal));

            log.Texts.Clear();
            var byWarehouse = new MovementFilter(null, fixture.Reference.DefaultWarehouseId, null, null, null);
            await reader.CountAsync(byWarehouse, null, default);
            await reader.PageAsync(byWarehouse, null, default);
            Assert.NotEmpty(log.Texts);
            Assert.All(log.Texts, t => Assert.DoesNotContain(Hint, t, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_product_with_no_movements_gets_an_empty_report()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var (reader, db, _) = NewReader();
        await using (db)
        {
            var filter = new MovementFilter(null, null, product.Id, null, null);
            Assert.Equal(0, await reader.CountAsync(filter, null, default));
            Assert.Empty(await reader.PageAsync(filter, null, default));
        }
    }
}
