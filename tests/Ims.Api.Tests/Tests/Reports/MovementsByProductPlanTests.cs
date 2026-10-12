using System.Data.Common;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Reports;

/// <summary>
/// The movements report for one product asks SQL Server for a plan of its own on every call. Without it the cached plan of the first product serves
/// all products, and the plan that suits a product with thousands of lines reads every document when the product has none (2.4 million reads at
/// 450,000 lines, docs/dba/README.md section 4b). Plan quality cannot be asserted in a test without being flaky; what is asserted is that the page
/// and both counts carry the hint when a product is filtered, that other filters do not, and that a product with no movements gets an empty report.
/// The requests go through the real host, so the interceptor registration in <c>AddInfrastructure</c> is what is tested; the only addition is a log
/// of the command text, registered after it.
/// </summary>
[Collection(ApiCollection.Name)]
public class MovementsByProductPlanTests(ApiFixture fixture)
{
    private const string Hint = "OPTION (RECOMPILE)";
    private const string LineTable = "FROM [StockDocumentLine]";

    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly List<string> _texts = [];

        public IReadOnlyList<string> Lines()
        {
            lock (_texts) return _texts.Where(t => t.Contains(LineTable, StringComparison.Ordinal)).ToList();
        }

        public void Clear()
        {
            lock (_texts) _texts.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Add(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Add(command);
            return ValueTask.FromResult(result);
        }

        private void Add(DbCommand command)
        {
            lock (_texts) _texts.Add(command.CommandText);
        }
    }

    [Fact]
    public async Task A_product_filter_marks_the_page_and_both_counts_for_recompile_and_other_filters_do_not()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var warehouse = fixture.Reference.DefaultWarehouseId;
        await s.ReceiveAsync(warehouse, new Line(product.Id, 4, 2m));

        var log = new CommandLog();
        using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var admin = ApiFixture.CreateClient(host);
        admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;

        var byProduct = (await admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>();
        Assert.Equal(product.Sku, Assert.Single(byProduct.Items).Sku);
        var marked = log.Lines();
        Assert.Equal(3, marked.Count);   // the page and the two counts (postings, reversals)
        Assert.All(marked, t => Assert.EndsWith(Hint, t, StringComparison.Ordinal));

        log.Clear();
        Assert.True((await admin.Reports.MovementsInWarehouseAsync(warehouse, 1, 10)).IsSuccess);
        Assert.NotEmpty(log.Lines());
        Assert.All(log.Lines(), t => Assert.DoesNotContain(Hint, t, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_product_with_no_movements_gets_an_empty_report()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var report = (await s.Admin.Reports.MovementsAsync(product.Id)).Data<PagedResult<MovementRow>>();

        Assert.Equal(0, report.Total);
        Assert.Empty(report.Items);
    }
}
