using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>Reading an open document costs no extra look-up of its warehouse: the integer key comes with the document, the public id is not translated back.</summary>
[Collection(ApiCollection.Name)]
public partial class DocumentReadQueryTests(ApiFixture fixture)
{
    /// <summary>The look-up an <c>IIdMap</c> makes: nothing but the integer and the public id of rows of the warehouse table.</summary>
    [GeneratedRegex(@"^SELECT \[\w+\]\.\[Id\], \[\w+\]\.\[PublicId\]\s+FROM \[Warehouse\]", RegexOptions.IgnoreCase)]
    private static partial Regex WarehouseByPublicId();

    private sealed class CommandLog : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Reading_an_open_document_does_not_translate_its_warehouse_id_with_a_query_of_its_own()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 5, 1m));
        var draft = await s.DraftIssueAsync(s.Admin, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1));
        var log = new CommandLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<Ims.Infrastructure.Persistence.AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        log.Commands.Clear();

        var read = (await client.Documents.GetAsync(draft.Id)).Data<DocumentDto>();

        Assert.NotEmpty(log.Commands); // the interceptor is in use
        Assert.DoesNotContain(log.Commands, c => WarehouseByPublicId().IsMatch(c));
        Assert.Equal(5m, Assert.Single(read.Lines).Available); // the stock was still looked up in the right warehouse

        // Control: a request that really translates a warehouse id (the list filter) does send that look-up, so the pattern above can fail.
        log.Commands.Clear();
        (await client.Documents.ListAsync(warehouseId: fixture.Reference.DefaultWarehouseId)).ShouldBeOk();
        Assert.Contains(log.Commands, c => WarehouseByPublicId().IsMatch(c));
    }
}
