using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// The commands of <c>GET /api/sales-orders/{id}</c>, pinned: a Draft order reads in a fixed number of commands, a submitted one asks once more for the people on it (the submitter, found by key).
/// An Approved or later order asks twice more (the posted quantities per line and the list of its deliveries). A further query slipping in shows here.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderReadStatementCountTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int DraftOrderCommands = 4;
    private const int PeopleQuery = 1; // the submitter is looked up by key once an order has been submitted
    private const int ApprovalQueries = 2; // an order waiting for a reviewer asks the rules twice: which thresholds it reaches, and whether the caller's tier satisfies them
    private const int DeliveryQueries = 2; // posted quantities per line, and the list of deliveries: only an order that can have deliveries (Approved and later) asks

    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    [Fact]
    public async Task A_submitted_order_reads_the_people_and_the_rules_more_than_a_draft_and_an_approved_one_the_people_and_the_deliveries()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var draft = await _s.DraftWorthAsync(10m);
        var submitted = await _s.SubmittedWorthAsync(6000m); // waiting for a reviewer
        var approved = await _s.SubmittedWorthAsync(100m);   // released at once

        async Task<int> CommandsAsync(Guid id)
        {
            (await client.SalesOrders.GetAsync(id)).ShouldBeOk(); // the first request of a host does work once
            log.Clear();
            (await client.SalesOrders.GetAsync(id)).ShouldBeOk();
            return log.Count;
        }

        Assert.Equal(DraftOrderCommands, await CommandsAsync(draft.Id));
        Assert.Equal(DraftOrderCommands + PeopleQuery + ApprovalQueries, await CommandsAsync(submitted.Id));
        Assert.Equal(DraftOrderCommands + PeopleQuery + DeliveryQueries, await CommandsAsync(approved.Id));
    }
}
