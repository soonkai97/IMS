using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Documents;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Returns;

/// <summary>
/// PL3.17: the number of SQL commands and of writes of the return use cases, frozen, next to the same operations on an ordinary receipt (<c>PostingStatementCountTests</c>). A return costs more than a receipt in
/// reads only: the original's lock, the version stamp, the original with its lines, the returned-quantity sum. Its writes are those of the same document without a link. The operations of a document that is not a return
/// are pinned in the other test and did not change, except that reversing a Posted receipt or sale issue asks for its returns (+2 commands). To change a number on purpose: say why in HANDOFFS.md and update <see cref="Frozen"/>
/// in the same pull request as the code that moves it.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReturnStatementCountTests(ApiFixture fixture)
{
    /// <summary>(commands, writes) per operation on a return of one product.</summary>
    private static readonly Dictionary<string, (int Commands, int Writes)> Frozen = new()
    {
        ["create return"] = (17, 4),
        ["submit return"] = (12, 2),
        ["post return"] = (15, 5),
        ["post receipt (for comparison)"] = (8, 5),
        ["reverse return"] = (10, 4),
    };

    [Fact]
    public async Task The_return_use_cases_send_the_frozen_number_of_statements()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        await Sales.SalesScenario.ResetAsync(fixture);
        foreach (var module in new[] { "purchasing", "sales", "returns" })
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", $"Module.{module}"));
        try
        {
            var s = await StockScenario.CreateAsync(fixture);
            var warm = await CycleAsync(s, client, log);                     // the first requests of a host do work once
            var measured = await CycleAsync(s, client, log);

            Assert.True(warm.Select(m => (m.Key, m.Value.Commands, m.Value.Writes)).OrderBy(x => x.Key).SequenceEqual(measured.Select(m => (m.Key, m.Value.Commands, m.Value.Writes)).OrderBy(x => x.Key)),
                "an operation costs the same on a second run");
            var moved = measured.Where(m => !Frozen.TryGetValue(m.Key, out var f) || f != (m.Value.Commands, m.Value.Writes)).ToList();
            Assert.True(moved.Count == 0, "Statement counts moved from the freeze (commands, writes):\n" + string.Join('\n', moved.Select(m =>
                $"[\"{m.Key}\"] = ({m.Value.Commands}, {m.Value.Writes}),  frozen: {(Frozen.TryGetValue(m.Key, out var f) ? f.ToString() : "none")}\n    " + string.Join("\n    ", m.Value.Statements.Select(t => string.Concat(t.ReplaceLineEndings(" ").Take(150)))))));
        }
        finally
        {
            await Sales.SalesScenario.ResetAsync(fixture);
        }
    }

    private async Task<Dictionary<string, Measure>> CycleAsync(StockScenario s, ApiClient client, SqlStatementLog log)
    {
        var measured = new Dictionary<string, Measure>();
        async Task MeasureAsync(string operation, Guid id, Func<string, Task<ApiResponse>> request)
        {
            var token = (await client.Documents.GetAsync(id)).Data<DocumentDto>().ConcurrencyToken;
            log.Clear();
            var response = await request(token);
            measured[operation] = new Measure(log.Count, log.WriteCount, log.Texts);
            response.ShouldBeOk();
        }

        var warehouse = s.Reference.DefaultWarehouseId;
        var product = await s.NewProductAsync();
        var receipt = await s.DraftReceiptAsync(s.Admin, warehouse, new Line(product.Id, 10m, 2m));
        await client.Documents.SubmitAsync(receipt.Id);
        await MeasureAsync("post receipt (for comparison)", receipt.Id, t => client.Documents.PostAsync(receipt.Id, t));
        var sale = await s.IssueAsync(warehouse, new Line(product.Id, 4m));

        log.Clear();
        var draft = (await client.Returns.CreateAsync(sale.Id)).Data<DocumentDto>();
        measured["create return"] = new Measure(log.Count, log.WriteCount, log.Texts);
        await MeasureAsync("submit return", draft.Id, t => client.Documents.SubmitAsync(draft.Id, t));
        (await s.Supervisor.Documents.ApproveAsync(draft.Id)).ShouldBeOk();
        await MeasureAsync("post return", draft.Id, t => client.Documents.PostAsync(draft.Id, t));
        await MeasureAsync("reverse return", draft.Id, t => client.Documents.ReverseAsync(draft.Id, "Entered twice", t));
        return measured;
    }

    private sealed record Measure(int Commands, int Writes, IReadOnlyList<string> Statements);
}
