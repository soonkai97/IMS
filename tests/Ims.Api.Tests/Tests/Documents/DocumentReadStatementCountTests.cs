using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Api.Tests.Tests.Purchasing;
using Ims.Application.Features.Auth;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// PL2.4 acceptance 7: reading a document that is NOT linked to a purchase order sends the same number of commands as before the link existed (the left join to the order is inside the one
/// statement that reads the document, and nothing asks whether the Purchasing module is on, because there is no link to show). Reading a linked document asks the module switch once (one command more).
/// The frozen numbers for posting and reading a posted document are in <see cref="PostingStatementCountTests"/>; this pins the read of a Draft and of a linked receipt too.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentReadStatementCountTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Commands of <c>GET /api/documents/{id}</c> for an OPEN document: the header, the lines, the stock on hand of the lines, and the settings for <c>allowedActions</c> (a posted one has no stock lookup: 3, frozen in the posting test).</summary>
    private const int OpenDocumentCommands = 4;

    /// <summary>A linked document asks the Purchasing switch once more (the module setting is read to decide whether the link is shown).</summary>
    private const int LinkedExtraCommands = 1;

    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    [Fact]
    public async Task Reading_a_document_that_is_not_linked_costs_the_frozen_number_of_commands_and_one_more_for_a_linked_one()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var product = await _p.NewProductAsync();
        var plain = await _p.Stock.DraftReceiptAsync(_p.Admin, _p.Warehouse, new Line(product.Id, 2m, 1m));
        var linked = await _p.ReceiptFromAsync(await _p.ApprovedAsync((product.Id, 2m, 1m, null)), _p.Admin);

        async Task<int> CommandsAsync(Guid id)
        {
            (await client.Documents.GetAsync(id)).ShouldBeOk(); // the first request of a host does work once (settings, access snapshot)
            log.Clear();
            (await client.Documents.GetAsync(id)).ShouldBeOk();
            return log.Count;
        }

        var unlinked = await CommandsAsync(plain.Id);
        var withLink = await CommandsAsync(linked.Id);

        Assert.Equal(OpenDocumentCommands, unlinked);
        Assert.Equal(OpenDocumentCommands + LinkedExtraCommands, withLink);
    }
}
