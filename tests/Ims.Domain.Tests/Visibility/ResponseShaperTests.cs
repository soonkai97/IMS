using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Stock;

namespace Ims.Domain.Tests.Visibility;

/// <summary>The one cost rule: a value tagged with a permission is null in the response of a caller who lacks it, wherever it sits in the response.</summary>
public class ResponseShaperTests
{
    private static readonly Func<string, bool> Nothing = _ => false;
    private static readonly Func<string, bool> CostOnly = p => p == Permissions.CostView;
    private static readonly Func<string, bool> Everything = _ => true;

    private record Line(int Qty, [property: VisibleWith(Permissions.CostView)] decimal? Cost);

    private record Doc(string No, IReadOnlyList<Line> Lines, [property: VisibleWith(Permissions.CostView)] decimal? Total, [property: VisibleWith(Permissions.ValuationView)] decimal? Worth);

    private record Page<T>(IReadOnlyList<T> Items, int Total);

    private static Doc NewDoc() => new("D1", [new Line(2, 3m), new Line(5, 4m)], 26m, 99m);

    [Fact]
    public void ACallerWithoutAnyPermission_SeesNoCostOrValueAnywhereInTheResponse()
    {
        var page = new Page<Doc>([NewDoc(), NewDoc()], 2);

        ResponseShaper.Shape(page, Nothing);

        Assert.All(page.Items, d => Assert.Null(d.Total));
        Assert.All(page.Items, d => Assert.Null(d.Worth));
        Assert.All(page.Items, d => Assert.All(d.Lines, l => Assert.Null(l.Cost)));
        Assert.Equal([2, 5], page.Items[0].Lines.Select(l => l.Qty)); // quantities stay
        Assert.Equal(2, page.Total);
    }

    [Fact]
    public void CostViewShowsCost_ButNotValuation_AndEveryPermissionShowsAll()
    {
        var clerk = NewDoc();
        var supervisor = NewDoc();

        ResponseShaper.Shape(clerk, CostOnly);
        ResponseShaper.Shape(supervisor, Everything);

        Assert.Equal(26m, clerk.Total);
        Assert.Null(clerk.Worth);
        Assert.Equal([3m, 4m], clerk.Lines.Select(l => l.Cost));
        Assert.Equal((26m, 99m), (supervisor.Total, supervisor.Worth));
    }

    [Fact]
    public void ABareListOfRules_IsShapedToo_AndAQuantityIsNotMoney()
    {
        var rules = new List<ApprovalRuleDto> { new(1, DocumentType.Issue, 500m, 10m, Roles.Supervisor, true) };

        ResponseShaper.Shape(rules, Nothing);

        Assert.Null(rules[0].AmountThreshold);
        Assert.Equal(10m, rules[0].QtyThreshold);
    }

    [Fact]
    public void AnApprovalTriggerOnAnAmount_HidesTheAmountsButNotTheKindOfRule()
    {
        var amount = new ApprovalTriggerDto(ApprovalMetric.Amount, 1000m, 1500m, Roles.Owner);
        var quantity = new ApprovalTriggerDto(ApprovalMetric.Quantity, 50m, 80m, Roles.Supervisor);

        ResponseShaper.Shape(new[] { amount, quantity }, Nothing);

        Assert.Equal((ApprovalMetric.Amount, Roles.Owner), (amount.Metric, amount.RoleRequired));
        Assert.Null(amount.Threshold);
        Assert.Null(amount.Actual);
        Assert.Equal((50m, 80m), (quantity.Threshold, quantity.Actual));
    }

    [Fact]
    public void TheStockTotalTheRowsAndTheMovements_NeedTheirPermission()
    {
        var summary = new StockSummary(1234m, 5, 2);
        var row = new StockRow(1, "S", "P", null, "PCS", 1, "W", "W", 3m, 0m, 2m, 6m, 0m, false, false);
        var ledger = new LedgerRow(1, DateTime.UtcNow, "S", "P", "W", "W", 3m, 2m, LedgerReason.Receipt, 1, "RCV", "u");
        var movement = new MovementRow(DateTime.UtcNow, "RCV", DocumentType.Receipt, DocumentStatus.Posted, "W", null, "S", "P", 3m, "PCS", 3m, 3m, 2m, 6m, false);

        foreach (var shaped in new object[] { summary, row, ledger, movement }) ResponseShaper.Shape(shaped, CostOnly);

        Assert.Null(summary.TotalValue);
        Assert.Equal((5, 2), (summary.SkuCount, summary.LowCount));
        Assert.Null(row.AvgCost);
        Assert.Null(row.Value);
        Assert.Equal(3m, row.QtyOnHand);
        Assert.Null(ledger.UnitCost);
        Assert.Equal((2m, 6m), (movement.UnitCost, movement.Amount)); // a movement is document data: cost.view is enough
    }

    [Fact]
    public void AnAuditPayload_LosesTheKeysThatNameMoney_AtAnyDepth()
    {
        const string json = "{\"Status\":\"Submitted\",\"Amount\":120.5,\"AmountThreshold\":10,\"Nested\":{\"AvgCost\":1,\"Name\":\"x\"},\"List\":[{\"UnitCost\":3,\"Qty\":1}]}";
        var row = new AuditLogDto(1, "admin", "document.submitted", "StockDocument", "RCV-1", json, json, DateTime.UtcNow);

        ResponseShaper.Shape(row, Nothing);

        Assert.Equal("{\"Status\":\"Submitted\",\"Nested\":{\"Name\":\"x\"},\"List\":[{\"Qty\":1}]}", row.After);
        Assert.Equal(row.After, row.Before);
    }

    [Fact]
    public void AnAuditPayload_KeepsTheCostForCostView_ButNotWhatTheStockIsWorth()
    {
        // UnitCost is a document line's cost (cost.view) and also the ledger's cost (valuation.view): withheld unless the caller has both.
        var row = new AuditLogDto(1, "x", "a", "e", "id", "{\"Amount\":120.5,\"AvgCost\":1,\"UnitCost\":2}", null, DateTime.UtcNow);

        ResponseShaper.Shape(row, CostOnly);

        Assert.Equal("{\"Amount\":120.5}", row.Before);
    }

    [Fact]
    public void ABrokenAuditPayload_IsDroppedRatherThanShown()
    {
        var row = new AuditLogDto(1, "x", "a", "e", "id", "{not json", null, DateTime.UtcNow);

        ResponseShaper.Shape(row, Nothing);

        Assert.Null(row.Before);
    }

    // ----- money precision: what a caller may see is also rounded to 2 decimals -----
    [Fact]
    public void AVisibleMoneyValue_IsRoundedToACent_HalfAwayFromZero()
    {
        var doc = new Doc("D1", [new Line(2, 0.005m), new Line(5, 10.0049999m)], 26.125m, -0.005m);

        ResponseShaper.Shape(doc, Everything);

        Assert.Equal([0.01m, 10.00m], doc.Lines.Select(l => l.Cost));
        Assert.Equal((26.13m, -0.01m), (doc.Total, doc.Worth));
    }

    [Fact]
    public void ANumberThatIsNotMoney_IsLeftAlone()
    {
        var quantity = new ApprovalTriggerDto(ApprovalMetric.Quantity, 50.12345m, 80.54321m, Roles.Supervisor);
        var amount = new ApprovalTriggerDto(ApprovalMetric.Amount, 1000m, 1500.005m, Roles.Owner);

        ResponseShaper.Shape(new[] { quantity, amount }, Everything);

        Assert.Equal((50.12345m, 80.54321m), (quantity.Threshold, quantity.Actual));
        Assert.Equal((1000m, 1500.01m), (amount.Threshold, amount.Actual));
    }

    private record NotNullable([property: VisibleWith(Permissions.CostView)] decimal Cost);

    [Fact]
    public void ATaggedPropertyThatCannotBeNull_FailsLoudlyInsteadOfLeaking()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ResponseShaper.Shape(new NotNullable(5m), Nothing));

        Assert.Contains("must be nullable", ex.Message);
    }
}
