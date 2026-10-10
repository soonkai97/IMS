using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Concurrency;

/// <summary>
/// BR-01 / BR-12 against the real SQL Server: parallel postings must never deadlock, lose an update, double-deduct or oversell.
/// Documents are prepared one by one; only the posting is fired in parallel.
/// </summary>
[Collection(ApiCollection.Name)]
public class ParallelPostingTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static string Summary(IEnumerable<ApiResponse> responses) =>
        string.Join(", ", responses.GroupBy(r => (int)r.Status).OrderBy(g => g.Key).Select(g => $"{g.Count()}x{g.Key}"));

    private static string FirstFailures(IEnumerable<ApiResponse> responses) =>
        string.Join(" | ", responses.Where(r => !r.IsSuccess).Take(3).Select(r => $"{(int)r.Status} {r.Body}"));

    private async Task<List<DocumentDto>> SubmittedIssuesAsync(StockScenario s, int count, Func<int, Line[]> linesFor)
    {
        var submitted = new List<DocumentDto>();
        for (var i = 0; i < count; i++)
            submitted.Add(await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, Warehouse, linesFor(i))));
        return submitted;
    }

    private static async Task AssertBalanceEqualsLedgerAsync(StockScenario s, ProductDto product, int warehouseId, decimal expected)
    {
        Assert.Equal(expected, await s.OnHandAsync(product.Id, warehouseId));
        Assert.Equal(expected, await s.LedgerSumAsync(product.Id, warehouseId));
    }

    [Fact]
    public async Task Forty_parallel_issues_on_the_same_two_products_with_lines_in_opposite_order_all_succeed()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(a.Id, 1000, 1m), new Line(b.Id, 1000, 1m));
        var issues = await SubmittedIssuesAsync(s, 40, i => i % 2 == 0
            ? [new Line(a.Id, 1), new Line(b.Id, 1)]
            : [new Line(b.Id, 1), new Line(a.Id, 1)]);

        var responses = await Concurrently.RunAsync(issues.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        Assert.True(responses.All(r => r.Status == HttpStatusCode.OK), $"Posting results: {Summary(responses)}. {FirstFailures(responses)}");
        await AssertBalanceEqualsLedgerAsync(s, a, Warehouse, 960);
        await AssertBalanceEqualsLedgerAsync(s, b, Warehouse, 960);
        foreach (var issue in issues)
            Assert.Equal(2, (await s.LedgerAsync(sourceDocId: issue.Id)).Count); // none lost, none doubled
        Assert.All(responses, r => Assert.Equal(DocumentStatus.Posted, r.Read<DocumentDto>().Status));
    }

    [Fact]
    public async Task Receipts_and_issues_posted_together_keep_the_balance_equal_to_the_ledger()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 500, 2m));

        var receipts = new List<DocumentDto>();
        for (var i = 0; i < 20; i++)
            receipts.Add(await s.SubmitAsync(s.Admin, await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 5, 3m))));
        var issues = await SubmittedIssuesAsync(s, 20, _ => [new Line(product.Id, 3)]);
        var all = receipts.Concat(issues).OrderBy(_ => Random.Shared.Next()).ToList(); // interleaved

        var responses = await Concurrently.RunAsync(all.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        Assert.True(responses.All(r => r.Status == HttpStatusCode.OK), $"Posting results: {Summary(responses)}. {FirstFailures(responses)}");
        await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, 500 + 20 * 5 - 20 * 3);
        var balance = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.InRange(balance.AvgCost!.Value, 2m, 3m);
    }

    [Fact]
    public async Task Two_parallel_posts_of_the_same_document_post_it_once()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var expected = 0m;

        for (var round = 1; round <= 5; round++)
        {
            var submitted = await s.SubmitAsync(s.Admin, await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 10, 1m)));

            var responses = await Concurrently.RunAsync(Enumerable.Range(0, 2)
                .Select(_ => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(submitted.Id))));

            Assert.True(responses.Count(r => r.Status == HttpStatusCode.OK) == 1, $"Round {round}: {Summary(responses)}. {FirstFailures(responses)}");
            Assert.True(responses.Single(r => !r.IsSuccess).StatusCode is >= 400 and < 500, $"Round {round}: the loser got {Summary(responses)}");
            expected += 10;
            Assert.Single(await s.LedgerAsync(sourceDocId: submitted.Id));
            await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, expected);
        }
    }

    [Fact]
    public async Task Two_parallel_posts_of_the_same_issue_deduct_once()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 1m));
        var issue = (await SubmittedIssuesAsync(s, 1, _ => [new Line(product.Id, 30)])).Single();

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 2)
            .Select(_ => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(issue.Id))));

        Assert.True(responses.Count(r => r.Status == HttpStatusCode.OK) == 1, Summary(responses));
        Assert.True(responses.Single(r => !r.IsSuccess).StatusCode is >= 400 and < 500, Summary(responses));
        await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, 70);
    }

    [Fact]
    public async Task Parallel_issues_against_limited_stock_never_oversell()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        var issues = await SubmittedIssuesAsync(s, 25, _ => [new Line(product.Id, 1)]); // each passes the submit check alone

        var responses = await Concurrently.RunAsync(issues.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        var succeeded = responses.Count(r => r.Status == HttpStatusCode.OK);
        Assert.DoesNotContain(responses, r => r.StatusCode >= 500);
        Assert.All(responses.Where(r => !r.IsSuccess), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        // Hard invariants first: never negative, never double-deducted, balance equals ledger.
        var onHand = await s.OnHandAsync(product.Id, Warehouse);
        Assert.True(onHand >= 0, $"Stock went negative: {onHand}");
        await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, 10 - succeeded);
        // Then the expected outcome: exactly the available units were sold, and the rest were told why.
        Assert.True(succeeded == 10, $"Expected exactly 10 postings to succeed but {succeeded} did. {Summary(responses)}. {FirstFailures(responses)}");
        Assert.All(responses.Where(r => !r.IsSuccess), r => Assert.Equal("Insufficient stock", r.Problem.Title));
    }

    [Fact]
    public async Task Parallel_posting_in_different_warehouses_does_not_interfere()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 1m));
        await s.ReceiveAsync(other.Id, new Line(product.Id, 100, 1m));
        var here = new List<DocumentDto>();
        var there = new List<DocumentDto>();
        for (var i = 0; i < 10; i++)
        {
            here.Add(await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 2))));
            there.Add(await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, other.Id, new Line(product.Id, 3))));
        }

        var responses = await Concurrently.RunAsync(here.Concat(there).Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        Assert.True(responses.All(r => r.Status == HttpStatusCode.OK), $"{Summary(responses)}. {FirstFailures(responses)}");
        await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, 80);
        await AssertBalanceEqualsLedgerAsync(s, product, other.Id, 70);
    }

    [Fact]
    public async Task Parallel_submits_of_the_same_draft_submit_it_once()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 3)
            .Select(_ => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.SubmitAsync(draft.Id))));

        Assert.True(responses.Count(r => r.Status == HttpStatusCode.OK) == 1, Summary(responses));
        Assert.DoesNotContain(responses, r => r.StatusCode >= 500);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task A_parallel_post_and_reversal_of_unrelated_documents_on_one_product_stay_consistent()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipts = new List<DocumentDto>();
        for (var i = 0; i < 6; i++) receipts.Add(await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m)));
        var toReverse = receipts.Take(3).ToList();
        var toPost = new List<DocumentDto>();
        for (var i = 0; i < 6; i++)
            toPost.Add(await s.SubmitAsync(s.Admin, await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 4, 1m))));

        var work = toReverse.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.ReverseAsync(d.Id, "parallel reversal")))
            .Concat(toPost.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));
        var responses = await Concurrently.RunAsync(work);

        Assert.True(responses.All(r => r.Status == HttpStatusCode.OK), $"{Summary(responses)}. {FirstFailures(responses)}");
        await AssertBalanceEqualsLedgerAsync(s, product, Warehouse, 60 - 30 + 24);
    }
}
