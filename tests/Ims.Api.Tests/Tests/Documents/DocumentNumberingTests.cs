using System.Net;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>BR-11: numbers are PREFIX-WAREHOUSE-yyMM-running, unique and gap-free per document type, warehouse and month.</summary>
[Collection(ApiCollection.Name)]
public class DocumentNumberingTests(ApiFixture fixture)
{
    /// <summary>The month part the server will use (Malaysia time, UTC+8).</summary>
    private static string ThisMonth()
    {
        var local = DateTime.UtcNow.AddHours(8);
        return $"{local.Year % 100:D2}{local.Month:D2}";
    }

    private static int RunningPart(string number) => int.Parse(Regex.Match(number, @"-(\d{4})$").Groups[1].Value);

    [Fact]
    public async Task Numbers_carry_prefix_warehouse_month_and_a_running_number_per_type()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var line = new Line(product.Id, 1, 1m);

        var r1 = await s.DraftReceiptAsync(s.Admin, warehouse.Id, line);
        var r2 = await s.DraftReceiptAsync(s.Admin, warehouse.Id, line);
        var i1 = await s.DraftIssueAsync(s.Admin, warehouse.Id, line);

        Assert.Equal($"RCV-{warehouse.Code}-{ThisMonth()}-0001", r1.No);
        Assert.Equal($"RCV-{warehouse.Code}-{ThisMonth()}-0002", r2.No);
        Assert.Equal($"ISS-{warehouse.Code}-{ThisMonth()}-0001", i1.No);
    }

    [Fact]
    public async Task Each_warehouse_has_its_own_sequence()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var first = await s.NewWarehouseAsync();
        var second = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var line = new Line(product.Id, 1, 1m);

        await s.DraftReceiptAsync(s.Admin, first.Id, line);
        await s.DraftReceiptAsync(s.Admin, first.Id, line);
        var inSecond = await s.DraftReceiptAsync(s.Admin, second.Id, line);

        Assert.Equal($"RCV-{second.Code}-{ThisMonth()}-0001", inSecond.No);
    }

    [Fact]
    public async Task Cancelling_does_not_free_a_number_for_reuse()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var line = new Line(product.Id, 1, 1m);
        var first = await s.DraftReceiptAsync(s.Admin, warehouse.Id, line);
        (await s.Admin.Documents.CancelAsync(first.Id)).ShouldBeOk();

        var next = await s.DraftReceiptAsync(s.Admin, warehouse.Id, line);

        Assert.Equal(1, RunningPart(first.No));
        Assert.Equal(2, RunningPart(next.No));
    }

    [Fact]
    public async Task A_create_that_fails_does_not_use_up_a_number()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var good = new DocumentBuilder(warehouse.Id).WithLines(new Line(product.Id, 1, 1m)).Build();
        var bad = new DocumentBuilder(warehouse.Id).WithLines(new Line(Guid.NewGuid(), 1, 1m)).Build();
        (await s.Admin.Documents.CreateReceiptAsync(good)).ShouldBeOk();

        (await s.Admin.Documents.CreateReceiptAsync(bad)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.CreateReceiptAsync(bad)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var next = (await s.Admin.Documents.CreateReceiptAsync(good)).Data<DocumentDto>();

        Assert.Equal(2, RunningPart(next.No));
    }

    [Fact]
    public async Task Twenty_parallel_creates_get_twenty_different_gap_free_numbers()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var request = new DocumentBuilder(warehouse.Id).WithLines(new Line(product.Id, 1, 1m)).Build();

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 20).Select(_ => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.CreateReceiptAsync(request))));

        AssertAllCreated(responses);
        var numbers = responses.Select(r => r.Read<DocumentDto>().No).ToList();
        Assert.Equal(20, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 20), numbers.Select(RunningPart).Order());
        Assert.All(numbers, n => Assert.StartsWith($"RCV-{warehouse.Code}-{ThisMonth()}-", n));
    }

    [Fact]
    public async Task Parallel_receipts_and_issues_each_keep_their_own_gap_free_sequence()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var receipt = new DocumentBuilder(warehouse.Id).WithLines(new Line(product.Id, 1, 1m)).Build();
        var issue = new DocumentBuilder(warehouse.Id).WithLines(new Line(product.Id, 1)).Build();

        var work = Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            (Func<Task<ApiResponse>>)(() => s.Admin.Documents.CreateReceiptAsync(receipt)),
            () => s.Admin.Documents.CreateIssueAsync(issue)
        });
        var responses = await Concurrently.RunAsync(work);

        AssertAllCreated(responses);
        var documents = responses.Select(r => r.Read<DocumentDto>()).ToList();
        foreach (var type in new[] { DocumentType.Receipt, DocumentType.Issue })
            Assert.Equal(Enumerable.Range(1, 10), documents.Where(d => d.Type == type).Select(d => RunningPart(d.No)).Order());
    }

    private static void AssertAllCreated(IReadOnlyList<ApiResponse> responses)
    {
        var failures = responses.Where(r => r.Status != HttpStatusCode.OK).Select(r => $"{(int)r.Status}: {r.Body}").ToList();
        Assert.True(failures.Count == 0, $"{failures.Count} of {responses.Count} creates failed: {string.Join(" | ", failures.Take(3))}");
    }
}
