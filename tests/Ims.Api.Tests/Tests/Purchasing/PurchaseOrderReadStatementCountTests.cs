using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// The commands of <c>GET /api/purchase-orders/{id}</c>, pinned: a Draft order reads without asking about receipts (none can exist), an Approved or later one asks for the people on it and twice more (the posted quantities per line
/// and the list of its receipts). A third query slipping in shows here.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderReadStatementCountTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int DraftOrderCommands = 4;
    private const int PeopleQuery = 1; // the submitter is looked up by key once an order has been submitted
    private const int ReceiptQueries = 2; // posted quantities per line, and the list of receipts

    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    [Fact]
    public async Task An_approved_order_reads_three_commands_more_than_a_draft_two_of_them_for_its_received_quantities_and_receipts()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var product = await _p.NewProductAsync();
        var draft = await _p.CreateAsync(_p.Warehouse, product.Id, _p.Clerk);
        var approved = await _p.ApprovedAsync((product.Id, 10m, 2m, null));

        async Task<int> CommandsAsync(Guid id)
        {
            (await client.PurchaseOrders.GetAsync(id)).ShouldBeOk(); // the first request of a host does work once
            log.Clear();
            (await client.PurchaseOrders.GetAsync(id)).ShouldBeOk();
            return log.Count;
        }

        Assert.Equal(DraftOrderCommands, await CommandsAsync(draft.Id));
        Assert.Equal(DraftOrderCommands + PeopleQuery + ReceiptQueries, await CommandsAsync(approved.Id));
    }
}
