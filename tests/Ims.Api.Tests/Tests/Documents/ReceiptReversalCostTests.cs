using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
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
    public async Task The_balance_equals_the_sum_of_the_ledger_after_a_reversed_receipt()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 50, 3m));
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 25, 9m));
        (await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Entered twice")).ShouldBeOk();

        var ledger = await s.LedgerAsync(product.Id, Warehouse);

        Assert.Equal(50m, ledger.Sum(r => r.QtyDelta));
        Assert.Equal(150m, ledger.Sum(r => r.QtyDelta * r.UnitCost));
        var balance = (await s.BalanceAsync(product.Id, Warehouse))!;
        Assert.Equal(150m, balance.Value);
    }
}
