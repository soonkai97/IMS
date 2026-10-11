using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>BR-02: a posted document is never edited; a reversal writes opposite ledger entries and restores the stock.</summary>
[Collection(ApiCollection.Name)]
public class ReversalTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task Reversing_a_receipt_writes_opposite_ledger_entries_and_returns_stock_to_where_it_was()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 4m));

        var reversed = (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Delivered to the wrong site")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Reversed, reversed.Status);
        Assert.Equal("Delivered to the wrong site", reversed.ReversalReason);
        Assert.Empty(reversed.AllowedActions);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));

        var entries = await s.LedgerAsync(sourceDocId: receipt.Id);
        Assert.Equal(2, entries.Count);
        var original = entries.Single(e => e.Reason == LedgerReason.Receipt);
        var reversal = entries.Single(e => e.Reason == LedgerReason.Reversal);
        Assert.Equal(10m, original.QtyDelta);
        Assert.Equal(-original.QtyDelta, reversal.QtyDelta);
        Assert.Equal(original.UnitCost, reversal.UnitCost);
        Assert.Equal(0m, entries.Sum(e => e.QtyDelta));
        Assert.Equal(0m, await s.LedgerSumAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Reversing_an_issue_puts_the_stock_back_at_the_cost_it_left_at()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 6m));
        var issue = await s.IssueAsync(Warehouse, new Line(product.Id, 10));
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));

        (await s.Admin.Documents.ReverseAsync(issue.Id, "Customer cancelled")).ShouldBeOk();

        var balance = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((10m, 6m), (balance.QtyOnHand, balance.AvgCost));
        var entries = await s.LedgerAsync(sourceDocId: issue.Id);
        Assert.Equal([-10m, 10m], entries.Select(e => e.QtyDelta).Order());
        Assert.All(entries, e => Assert.Equal(6m, e.UnitCost));
    }

    [Fact]
    public async Task A_reversed_document_says_who_reversed_it_and_when()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 2, 1m));
        Assert.All(new[] { receipt.ReversedBy, receipt.ReversedByName }, Assert.Null);
        Assert.Null(receipt.ReversedAt);
        var before = DateTime.UtcNow.AddSeconds(-5);

        var reversed = (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Delivered to the wrong site")).Data<DocumentDto>();
        var read = await s.GetAsync(receipt.Id);

        Assert.Equal(TestUsers.UsernameOf(Roles.Supervisor), reversed.ReversedBy);
        Assert.False(string.IsNullOrWhiteSpace(reversed.ReversedByName));
        Assert.InRange(reversed.ReversedAt!.Value, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal((reversed.ReversedBy, reversed.ReversedByName, reversed.ReversedAt), (read.ReversedBy, read.ReversedByName, read.ReversedAt));
        Assert.Equal("Delivered to the wrong site", read.ReversalReason);
    }

    [Fact]
    public async Task A_reversal_needs_a_reason()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "   ")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(DocumentStatus.Posted, (await s.GetAsync(receipt.Id)).Status);
    }

    [Fact]
    public async Task A_document_can_be_reversed_only_once()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));
        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "first")).ShouldBeOk();

        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "second")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(2, (await s.LedgerAsync(sourceDocId: receipt.Id)).Count);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Only_a_posted_document_can_be_reversed()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));
        var submitted = await s.SubmitAsync(s.Admin, await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m)));

        (await s.Admin.Documents.ReverseAsync(draft.Id, "not needed here")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.ReverseAsync(submitted.Id, "not needed here")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.Owner)]
    public async Task Only_a_supervisor_or_admin_may_reverse(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        (await s.As(role).Documents.ReverseAsync(receipt.Id, "no right")).ShouldBe(HttpStatusCode.Forbidden);

        Assert.Equal(3, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_reversal_leaves_the_original_ledger_entry_untouched_and_adds_exactly_one_per_line()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var a = await s.NewProductAsync();
        var b = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(a.Id, 2, 1m), new Line(b.Id, 3, 1m));
        var before = await s.LedgerAsync(sourceDocId: receipt.Id);

        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "undo the receipt")).ShouldBeOk();

        var after = await s.LedgerAsync(sourceDocId: receipt.Id);
        Assert.Equal(before.Count * 2, after.Count);
        foreach (var original in before)
            Assert.Contains(after, e => e.Id == original.Id && e.QtyDelta == original.QtyDelta && e.UnitCost == original.UnitCost);
    }
}
