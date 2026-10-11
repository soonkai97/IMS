using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>Money has 2 decimals: the unit cost entered, the line amount, the document total, an estimate and an approval threshold.</summary>
public class MoneyAmountTests
{
    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    // ----- the cost that is entered -----
    [Theory]
    [InlineData("1.234")]
    [InlineData("0.001")]
    [InlineData("10.005")]
    public void ALine_RefusesAUnitCostWithAFractionOfACent(string cost)
    {
        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(P(1), DocumentType.Receipt, 1m, null, decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture), null));

        Assert.Equal(Messages.CostTooPrecise, ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.2")]
    [InlineData("1.20")]
    [InlineData("0.01")]
    [InlineData("1234567.89")]
    public void ALine_AcceptsAUnitCostOfUpToTwoDecimals(string cost)
    {
        var value = decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture);

        var line = StockDocumentLine.Create(P(1), DocumentType.Receipt, 1m, null, value, null);

        Assert.Equal(value, line.UnitCost);
    }

    [Fact]
    public void AnIssueLine_IgnoresTheCostItWasGiven_SoItNeverFailsOnIt()
    {
        var line = StockDocumentLine.Create(P(1), DocumentType.Issue, 1m, null, null, null);

        Assert.Equal(0m, line.UnitCost);
    }

    // ----- line amount and total -----
    [Theory]
    [InlineData("1.5", "0.01", "0.02")]    // 0.015 goes up
    [InlineData("0.5", "0.01", "0.01")]    // 0.005 goes up
    [InlineData("3", "3.33", "9.99")]
    [InlineData("1000", "4.99", "4990.00")]
    public void ALineAmount_IsTheBaseQuantityTimesTheCost_RoundedToACent(string qty, string cost, string expected)
    {
        var line = StockDocumentLine.Create(P(1), DocumentType.Receipt, decimal.Parse(qty, System.Globalization.CultureInfo.InvariantCulture), null,
            decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture), null);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), line.Amount);
    }

    [Fact]
    public void ADocumentTotal_IsTheSumOfTheRoundedLineAmounts_NotTheRoundedSum()
    {
        // Three lines of 1.5 x 0.01 = 0.015: each is 0.02, so the document is 0.06. Rounding the exact sum (0.045) would say 0.05.
        var doc = NewReceipt(1, (P(1), 1.5m, 0.01m), (P(2), 1.5m, 0.01m), (P(3), 1.5m, 0.01m));

        Assert.Equal([0.02m, 0.02m, 0.02m], doc.Lines.Select(l => l.Amount));
        Assert.Equal(0.06m, doc.TotalAmount);
    }

    [Fact]
    public void ADocumentTotal_NeverHasAFractionOfACent()
    {
        var doc = NewReceipt(1, (P(1), 3m, 3.33m), (P(2), 0.5m, 0.01m), (P(3), 7.25m, 1.99m));

        Assert.True(Money.IsExact(doc.TotalAmount));
        Assert.Equal(9.99m + 0.01m + 14.43m, doc.TotalAmount);
    }

    // ----- estimates -----
    [Fact]
    public void AnEstimatedIssue_IsRoundedPerLine_FromAnAverageThatHasMorePlaces()
    {
        var doc = NewIssue(2, (P(1), 1m), (P(2), 1m));

        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 10.005m, [2] = 4.9949999999m });

        Assert.Equal([10.01m, 4.99m], doc.Lines.Select(l => l.Amount));
        Assert.Equal(15.00m, doc.TotalAmount);
    }

    [Fact]
    public void AnEstimateThatRoundsUpToTheThreshold_ReachesItsApprovalRule()
    {
        // 1,000 pieces at 4.9999996 are 4,999.9996: that is 5,000.00 to the cent, and a rule that starts at 5,000.00 fires.
        var doc = NewIssue(2, (P(1), 1000m));
        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 4.9999996m });
        var rule = ApprovalRule.Create(DocumentType.Issue, 5000m, null, Roles.Supervisor);

        Assert.Equal(5000.00m, doc.TotalAmount);
        Assert.True(rule.Matches(DocumentType.Issue, doc.TotalQty, doc.TotalAmount));
    }

    [Fact]
    public void AnEstimateThatRoundsDownBelowTheThreshold_DoesNotReachItsRule()
    {
        var doc = NewIssue(2, (P(1), 1000m));
        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 4.9999949m }); // 4,999.9949 is 4,999.99
        var rule = ApprovalRule.Create(DocumentType.Issue, 5000m, null, Roles.Supervisor);

        Assert.Equal(4999.99m, doc.TotalAmount);
        Assert.False(rule.Matches(DocumentType.Issue, doc.TotalQty, doc.TotalAmount));
    }

    // ----- approval thresholds -----
    [Theory]
    [InlineData("100.005")]
    [InlineData("0.001")]
    public void AnApprovalThreshold_RefusesAFractionOfACent(string amount)
    {
        var ex = Assert.Throws<DomainException>(() => ApprovalRule.Create(DocumentType.Issue, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), null, Roles.Supervisor));

        Assert.Equal(Messages.AmountTooPrecise, ex.Message);
    }

    [Fact]
    public void AnApprovalThreshold_OfUpToTwoDecimals_IsAccepted()
    {
        var rule = ApprovalRule.Create(DocumentType.Issue, 100.50m, null, Roles.Supervisor);

        Assert.Equal(100.50m, rule.AmountThreshold);
    }
}
