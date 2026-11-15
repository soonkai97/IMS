using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Sales;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// DDD-13: the number of SQL commands (database round trips) and of insert, update and delete statements inside them that each price-list save sends, measured on <c>main</c> before the audit moved to a
/// subscriber and frozen here. The subscriber names the SKU and the unit of each priced item in ONE batched lookup (a command of its own), so a save whose audit row has words costs one command more than before;
/// a save with no words (a list with no items, an update that changes no price, a customer change) costs the same. The writes do not move. A failure prints the statements of every operation that changed.
/// To change a number on purpose: say why in HANDOFFS.md and update <see cref="Frozen"/> in the same pull request as the code that moves it.
/// </summary>
[Collection(ApiCollection.Name)]
public class PriceListStatementCountTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string CreateThree = "create, three items";
    private const string CreateEmpty = "create, no items";
    private const string UpdateThree = "update, one added, one repriced, one removed";
    private const string UpdateSame = "update, nothing changed";
    private const string SetCustomer = "set customer";
    private const string Delete = "delete, two items";

    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    /// <summary>(commands, writes) per operation. Writes: the list and its items, the audit row, the row-version touch of the list.</summary>
    private static readonly Dictionary<string, (int Commands, int Writes)> Frozen = new()
    {
        [CreateEmpty] = (7, 2),
        [CreateThree] = (10, 3),
        [UpdateThree] = (11, 5),
        [UpdateSame] = (10, 1),
        [SetCustomer] = (8, 2),
        [Delete] = (5, 5),
    };

    [Fact]
    public async Task The_price_list_saves_send_the_frozen_number_of_statements()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;

        var warmUp = await CycleAsync(client, log);   // the first requests of a host do work once (settings, access snapshot)
        var actual = await CycleAsync(client, log);

        Assert.True(Totals(warmUp).SequenceEqual(Totals(actual)), "an operation costs the same on a second run: " + string.Join(", ", Totals(warmUp).Zip(Totals(actual)).Select(p => $"{p.First.Key} {p.First.Value}/{p.Second.Value}")));
        var moved = actual.Where(a => !Frozen.TryGetValue(a.Key, out var frozen) || frozen != (a.Value.Commands, a.Value.Writes)).ToList();
        Assert.True(moved.Count == 0 && Frozen.Count == actual.Count, "Statement counts moved from the freeze (commands, writes):\n" + string.Join('\n', moved.Select(m =>
            $"[\"{m.Key}\"] = ({m.Value.Commands}, {m.Value.Writes}),  frozen: {(Frozen.TryGetValue(m.Key, out var f) ? f.ToString() : "none")}\n    " + string.Join("\n    ", m.Value.Statements.Select(Short)))));
    }

    private async Task<Dictionary<string, Measure>> CycleAsync(ApiClient client, SqlStatementLog log)
    {
        var measured = new Dictionary<string, Measure>();
        // The version token each save needs is the one the last response carried, so no read of the list is counted with the save it belongs to.
        async Task<ApiResponse> MeasureAsync(string operation, Func<Task<ApiResponse>> request)
        {
            log.Clear();
            var response = await request();
            measured[operation] = new Measure(log.Count, log.WriteCount, log.Texts);
            Assert.True(response.IsSuccess, $"{operation}: HTTP {response.StatusCode}");
            return response;
        }

        var (a, b, c, d) = (await _s.NewProductAsync(), await _s.NewProductAsync(), await _s.NewProductAsync(), await _s.NewProductAsync());
        var customer = await _s.NewPartnerAsync();
        var name = Unique.Name("Counted");

        _ = await MeasureAsync(CreateEmpty, () => client.PriceLists.CreateAsync(_s.List(Unique.Name("Empty")).Build()));
        var created = await MeasureAsync(CreateThree, () => client.PriceLists.CreateAsync(_s.List(name).WithItem(a.Id, 1m).WithItem(b.Id, 2m).WithItem(c.Id, 3m).Build()));
        var list = created.Data<PriceListDto>();

        var token = list.ConcurrencyToken;
        var updated = await MeasureAsync(UpdateThree, () => client.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(a.Id, 1m).WithItem(b.Id, 2.5m).WithItem(d.Id, 4m).Build(), token));
        token = updated.Data<PriceListDto>().ConcurrencyToken;
        var same = await MeasureAsync(UpdateSame, () => client.PriceLists.UpdateAsync(list.Id, _s.List(name).WithItem(a.Id, 1m).WithItem(b.Id, 2.5m).WithItem(d.Id, 4m).Build(), token));
        token = same.Data<PriceListDto>().ConcurrencyToken;
        var set = await MeasureAsync(SetCustomer, () => client.PriceLists.SetCustomerAsync(list.Id, customer.Id, token));
        token = set.Data<PriceListDto>().ConcurrencyToken;
        _ = await MeasureAsync(Delete, () => client.PriceLists.DeleteAsync(list.Id, token));
        return measured;
    }

    private static IEnumerable<KeyValuePair<string, (int, int)>> Totals(Dictionary<string, Measure> measured) =>
        measured.OrderBy(m => m.Key).Select(m => KeyValuePair.Create(m.Key, (m.Value.Commands, m.Value.Writes)));

    private static string Short(string statement) => string.Concat(statement.ReplaceLineEndings(" ").Take(150));

    private sealed record Measure(int Commands, int Writes, IReadOnlyList<string> Statements);
}
