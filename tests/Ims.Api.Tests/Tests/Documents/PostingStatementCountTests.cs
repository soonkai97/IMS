using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Documents;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// PL1.0 (ADR modules-and-tracking-dimensions, rollout step 0): the number of SQL commands (database round trips) and of insert/update/delete statements inside them that the document
/// use cases of an UNTRACKED product send, frozen on main at c89b32b before any lot, module or pilot change. Posting rides on these (lock, load balances, write ledger, save, read
/// back), so a look-up that sneaks into the path of every document, or a row written more, shows up here. The lot design allows exactly one more command for an untracked document
/// (the tracking-mode read); until that slice exists the numbers must not move. A failure prints the statements of every operation that changed.
/// To change a number on purpose: say why in HANDOFFS.md, and update <see cref="Frozen"/> in the same pull request as the code that moves it.
/// </summary>
[Collection(ApiCollection.Name)]
public class PostingStatementCountTests(ApiFixture fixture)
{
    private const string SubmitReceipt = "submit receipt";
    private const string PostReceipt = "post receipt";
    private const string ReadPosted = "read posted document";
    private const string SubmitIssue = "submit issue";
    private const string PostIssue = "post issue";
    private const string ReverseIssue = "reverse issue";
    private const string ReverseReceipt = "reverse receipt";

    private const int OneProduct = 1;
    private const int ThreeProducts = 3;
    private const int StatementTextLength = 150;

    /// <summary>
    /// (commands, writes) per operation for a document of one product and of three. Posting and reversing take one application lock per product (two more commands for three
    /// products); the writes grow by one per product (its balance) and nothing else does. A submit is 8 commands for a receipt and 10 for an issue (it also reads the balances).
    /// </summary>
    private static readonly Dictionary<(string Operation, int Products), (int Commands, int Writes)> Frozen = new()
    {
        [(SubmitReceipt, OneProduct)] = (8, 2),
        [(PostReceipt, OneProduct)] = (8, 5),
        [(ReadPosted, OneProduct)] = (3, 0),
        [(SubmitIssue, OneProduct)] = (10, 2),
        [(PostIssue, OneProduct)] = (8, 4),
        [(ReverseIssue, OneProduct)] = (8, 4),
        [(ReverseReceipt, OneProduct)] = (8, 4),
        [(SubmitReceipt, ThreeProducts)] = (8, 2),
        [(PostReceipt, ThreeProducts)] = (10, 7),
        [(ReadPosted, ThreeProducts)] = (3, 0),
        [(SubmitIssue, ThreeProducts)] = (10, 2),
        [(PostIssue, ThreeProducts)] = (10, 6),
        [(ReverseIssue, ThreeProducts)] = (10, 6),
        [(ReverseReceipt, ThreeProducts)] = (10, 6),
    };

    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task The_document_use_cases_of_an_untracked_product_send_the_frozen_number_of_statements()
    {
        var log = new SqlStatementLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<AuthResult>().AccessToken;
        var s = await StockScenario.CreateAsync(fixture);

        var warmUp = await CycleAsync(s, client, log, OneProduct); // the first requests of a host do work once (settings, access snapshot)
        var one = await CycleAsync(s, client, log, OneProduct);
        var three = await CycleAsync(s, client, log, ThreeProducts);

        Assert.True(Totals(warmUp).SequenceEqual(Totals(one)), "an operation costs the same on a second run: " + string.Join(", ", Totals(warmUp).Zip(Totals(one)).Select(p => $"{p.First.Key} {p.First.Value}/{p.Second.Value}")));
        var actual = new Dictionary<(string Operation, int Products), Measure>();
        foreach (var (operation, measure) in one) actual[(operation, OneProduct)] = measure;
        foreach (var (operation, measure) in three) actual[(operation, ThreeProducts)] = measure;

        var moved = actual.Where(a => !Frozen.TryGetValue(a.Key, out var frozen) || frozen != (a.Value.Commands, a.Value.Writes)).ToList();
        Assert.True(moved.Count == 0 && Frozen.Count == actual.Count, "Statement counts moved from the freeze (commands, writes):\n" + string.Join('\n', moved.Select(m =>
            $"[(\"{m.Key.Operation}\", {m.Key.Products})] = ({m.Value.Commands}, {m.Value.Writes}),  frozen: {(Frozen.TryGetValue(m.Key, out var f) ? f.ToString() : "none")}\n    "
            + string.Join("\n    ", m.Value.Statements.Select(Short)))));
    }

    /// <summary>One receipt and one issue of <paramref name="products"/> new products, taken through submit, post, read and reversal; the operations on the measured host are counted.</summary>
    private async Task<Dictionary<string, Measure>> CycleAsync(StockScenario s, ApiClient client, SqlStatementLog log, int products)
    {
        var measured = new Dictionary<string, Measure>();
        // The version token is read before the count starts: reading it is the caller's own request, not part of the use case measured.
        async Task MeasureAsync(string operation, Guid id, Func<string, Task<ApiResponse>> request)
        {
            var token = (await client.Documents.GetAsync(id)).Data<DocumentDto>().ConcurrencyToken;
            log.Clear();
            var response = await request(token);
            measured[operation] = new Measure(log.Count, log.WriteCount, log.Texts);
            response.ShouldBeOk();
        }

        var items = new List<Guid>();
        for (var i = 0; i < products; i++) items.Add((await s.NewProductAsync()).Id);

        var receipt = await s.DraftReceiptAsync(s.Admin, Warehouse, items.Select(p => new Line(p, 10, 2m)).ToArray());
        await MeasureAsync(SubmitReceipt, receipt.Id, t => client.Documents.SubmitAsync(receipt.Id, t));
        await MeasureAsync(PostReceipt, receipt.Id, t => client.Documents.PostAsync(receipt.Id, t));
        await MeasureAsync(ReadPosted, receipt.Id, _ => client.Documents.GetAsync(receipt.Id));

        var issue = await s.DraftIssueAsync(s.Admin, Warehouse, items.Select(p => new Line(p, 4)).ToArray());
        await MeasureAsync(SubmitIssue, issue.Id, t => client.Documents.SubmitAsync(issue.Id, t));
        await MeasureAsync(PostIssue, issue.Id, t => client.Documents.PostAsync(issue.Id, t));
        await MeasureAsync(ReverseIssue, issue.Id, t => client.Documents.ReverseAsync(issue.Id, "Entered twice", t));
        await MeasureAsync(ReverseReceipt, receipt.Id, t => client.Documents.ReverseAsync(receipt.Id, "Entered twice", t));
        return measured;
    }

    private static IEnumerable<KeyValuePair<string, (int, int)>> Totals(Dictionary<string, Measure> measured) =>
        measured.OrderBy(m => m.Key).Select(m => KeyValuePair.Create(m.Key, (m.Value.Commands, m.Value.Writes)));

    private static string Short(string statement) => string.Concat(statement.ReplaceLineEndings(" ").Take(StatementTextLength));

    private sealed record Measure(int Commands, int Writes, IReadOnlyList<string> Statements);
}
