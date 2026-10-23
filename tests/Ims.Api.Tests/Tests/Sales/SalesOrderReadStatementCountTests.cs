using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// The commands of <c>GET /api/sales-orders/{id}</c>, pinned: a Draft order reads in a fixed number of commands, a submitted one asks once more for the people on it (the submitter, found by key).
/// There is no receiving side yet (deliveries arrive with PL3.5 to PL3.7). A further query slipping in shows here.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderReadStatementCountTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int DraftOrderCommands = 4;
    private const int PeopleQuery = 1; // the submitter is looked up by key once an order has been submitted

    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    [Fact]
    public async Task A_submitted_order_reads_one_command_more_than_a_draft_for_the_people_on_it()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var draft = await _s.DraftWorthAsync(10m);
        var submitted = await _s.SubmittedWorthAsync(100m);

        async Task<int> CommandsAsync(Guid id)
        {
            (await client.SalesOrders.GetAsync(id)).ShouldBeOk(); // the first request of a host does work once
            log.Clear();
            (await client.SalesOrders.GetAsync(id)).ShouldBeOk();
            return log.Count;
        }

        Assert.Equal(DraftOrderCommands, await CommandsAsync(draft.Id));
        Assert.Equal(DraftOrderCommands + PeopleQuery, await CommandsAsync(submitted.Id));
    }
}
