using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>PR-01: reversing a receipt takes its cost back out of the average, so the stock value and the estimate of a later issue are what they were before.</summary>
[Collection(ApiCollection.Name)]
public class ReceiptReversalCostTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static async Task<StockSummary> SummaryAsync(StockScenario s) => (await s.Admin.Stock.SummaryAsync()).Data<StockSummary>();

    [Fact]
    public async Task A_reversed_receipt_leaves_quantity_average_cost_stock_value_and_the_issue_estimate_as_they_were()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 735, 4.76m));
        var before = (await s.BalanceAsync(product.Id, Warehouse))!;
        var totalBefore = (await SummaryAsync(s)).TotalValue;

        // A duplicate delivery entry: 600 x 200.00 (120,000, so the Owner approves), posted, then reversed.
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 600, 200m));
        var during = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.True(during.AvgCost > 90m);
        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Duplicate delivery entry")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(735m, after.QtyOnHand);
        Assert.Equal(before.AvgCost, after.AvgCost);
        Assert.Equal(4.76m, after.AvgCost);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(totalBefore, (await SummaryAsync(s)).TotalValue);

        // A staff issue of 60 is worth 60 x 4.76 = 285.60: far below the 5,000.00 approval rule that the contaminated average (5,550.36) used to trigger.
        var issue = await s.SubmitAsync(s.Staff, await s.DraftIssueAsync(s.Staff, Warehouse, new Line(product.Id, 60)));
        Assert.False(issue.RequiresApproval);
        Assert.Empty(issue.ApprovalTriggers);
        var seen = (await s.Supervisor.Documents.GetAsync(issue.Id)).Data<DocumentDto>();
        Assert.Equal(285.6m, seen.TotalAmount);
    }

    [Fact]
    public async Task A_receipt_reversed_after_later_issues_gives_back_the_average_and_keeps_the_issued_units_out()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 735, 4.76m));
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 600, 200m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 60));

        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Duplicate delivery entry")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(675m, after.QtyOnHand);
        Assert.Equal(4.76m, after.AvgCost);
        Assert.Equal(3213m, after.Value);
    }

    [Fact]
    public async Task Reversing_the_first_of_two_receipts_leaves_the_second_at_its_own_cost()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var first = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 10m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 20m));

        (await s.Supervisor.Documents.ReverseAsync(first.Id, "Entered twice")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((100m, 20m, 2000m), (after.QtyOnHand, after.AvgCost, after.Value));
    }

    [Fact]
    public async Task The_stock_value_equals_the_value_of_the_ledger_after_receipts_and_a_reversal_without_issues()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 50, 3m));
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 25, 9m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 6m));
        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Entered twice")).ShouldBeOk();

        var ledger = await s.LedgerAsync(product.Id, Warehouse);

        var balance = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(60m, balance.QtyOnHand);
        Assert.Equal(60m, ledger.Sum(r => r.QtyDelta));
        Assert.Equal(210m, ledger.Sum(r => r.QtyDelta * r.UnitCost)); // 50 x 3 + 10 x 6
        Assert.InRange(balance.Value!.Value, 209.99999m, 210.00001m);  // the average keeps 10 places
    }

    // ----- review B1: a later receipt at the same cost must not make the first look like the latest -----
    [Fact]
    public async Task Two_receipts_at_the_same_cost_reverse_the_first_and_the_second_stays_at_that_cost()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var first = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 10m));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 10m));

        (await s.Supervisor.Documents.ReverseAsync(first.Id, "Entered twice")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((100m, 10m, 1000m), (after.QtyOnHand, after.AvgCost, after.Value));
    }

    [Fact]
    public async Task A_staff_receipt_at_the_average_after_the_receipt_does_not_hide_the_receipts_cost()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 50, 5m));
        var duplicate = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 10m));
        var staffDraft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 40)); // takes the average, whatever it is
        await s.CompleteAsync(await s.SubmitAsync(s.Staff, staffDraft));

        (await s.Supervisor.Documents.ReverseAsync(duplicate.Id, "Duplicate delivery entry")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(90m, after.QtyOnHand);
        Assert.InRange(after.Value!.Value, 583.33m, 583.34m); // 50 x 5 plus 40 units worth 8.33 each; the old code said 450 (90 x 5)
    }

    [Fact]
    public async Task A_receipt_in_cartons_is_taken_back_at_its_cost_per_piece()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 12));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 40, 4m));
        var cartons = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 120m, fixture.Reference.CartonUnitId)); // 60 pieces at 10.00
        Assert.Equal(100m, await s.OnHandAsync(product.Id, Warehouse));

        (await s.Supervisor.Documents.ReverseAsync(cartons.Id, "Wrong delivery")).ShouldBeOk();

        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((40m, 4m, 160m), (after.QtyOnHand, after.AvgCost, after.Value));
    }

    [Fact]
    public async Task Reversing_the_latest_receipt_twice_in_a_row_walks_back_through_the_receipts()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var first = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 10m));
        var second = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 20m));

        (await s.Supervisor.Documents.ReverseAsync(second.Id, "Wrong")).ShouldBeOk();
        var middle = (await s.BalanceAsync(product.Id, Warehouse))!;
        (await s.Supervisor.Documents.ReverseAsync(first.Id, "Wrong too")).ShouldBeOk();

        var end = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((100m, 10m), (middle.QtyOnHand, middle.AvgCost));
        Assert.Equal((0m, 0m, 0m), (end.QtyOnHand, end.AvgCost, end.Value));
    }

    [Fact]
    public async Task Two_simultaneous_reversals_of_the_same_receipt_leave_one_reversal_and_one_refusal()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 600, 200m));

        var responses = await Concurrently.RunAsync(Enumerable.Range(0, 2)
            .Select(_ => (Func<Task<ApiResponse>>)(() => s.Supervisor.Documents.ReverseAsync(receipt.Id, "Duplicate delivery entry", receipt.ConcurrencyToken))));

        Assert.Single(responses, r => r.IsSuccess);
        Assert.All(responses.Where(r => !r.IsSuccess), r => Assert.Contains((int)r.Status, new[] { 400, 409 }));
        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal((10m, 2m), (after.QtyOnHand, after.AvgCost)); // taken back once, not twice
        Assert.Equal(1, (await s.LedgerAsync(sourceDocId: receipt.Id)).Count(e => e.Reason == LedgerReason.Reversal));
    }

    [Fact]
    public async Task Simultaneous_reversals_of_two_receipts_of_one_product_both_succeed_and_agree_with_the_ledger()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 2m));
        var a = await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 20m));
        var b = await s.ReceiveAsync(Warehouse, new Line(product.Id, 50, 40m));

        var responses = await Concurrently.RunAsync(new[] { a, b }
            .Select(d => (Func<Task<ApiResponse>>)(() => s.Supervisor.Documents.ReverseAsync(d.Id, "Entered twice", d.ConcurrencyToken))));

        Assert.All(responses, r => Assert.True(r.IsSuccess, r.Body));
        var after = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(10m, after.QtyOnHand);
        Assert.Equal(10m, await s.LedgerSumAsync(product.Id, Warehouse));
        Assert.InRange(after.Value!.Value, 19.9m, 20.1m); // whichever came out first, 10 units worth 2 remain
    }
}
