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

    // ----- the amount is stored, computed once from the entered quantity and cost, with no division through the base unit -----
    [Theory]
    [InlineData("100000", "10.00", 7, "1000000.00")]       // per base 1.428571: rebuilt from it the line was 999,999.70
    [InlineData("50000", "12.34", 144, "617000.00")]       // rebuilt: 616,996.80
    [InlineData("1000000000", "1.00", 3, "1000000000.00")] // at the quantity limit, even 10 decimals drift (999,999,999.90)
    [InlineData("1000", "3.00", 1000, "3000.00")]
    public void ALineInACarton_HasTheAmountOfItsEnteredQuantityTimesTheEnteredCost(string cartons, string cost, int factor, string expected)
    {
        var product = NewProduct(cartonFactor: factor);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, D(cartons), CartonId, D(cost), null);

        Assert.Equal(D(expected), line.Amount);
        Assert.Equal(D(cost), line.EnteredUnitCost);          // read back as typed: saving the draft again does not re-price it
        Assert.Equal(D(cartons) * factor, line.BaseQty);
    }

    [Theory]
    [InlineData("0.0001", "49.99", "0.00")]   // 0.004999
    [InlineData("0.5555", "0.01", "0.01")]    // 0.005555
    [InlineData("33.3333", "0.03", "1.00")]   // 0.999999
    [InlineData("0.5", "0.01", "0.01")]       // 0.005, the boundary
    [InlineData("0.4999", "0.01", "0.00")]
    public void AFourDecimalQuantityTimesACost_IsRoundedOnceToACent(string qty, string cost, string expected)
    {
        var line = StockDocumentLine.Create(P(1), DocumentType.Receipt, D(qty), null, D(cost), null);

        Assert.Equal(D(expected), line.Amount);
    }

    [Fact]
    public void ACartonOfAThousandPieces_AtThree_CostsThreeTenthsOfACentAPiece_AndTheAmountsStayCorrect()
    {
        var product = NewProduct(cartonFactor: 1000);

        var line = StockDocumentLine.Create(product, DocumentType.Receipt, 1m, CartonId, 3.00m, null);

        Assert.Equal(0.003m, line.UnitCost);                    // per base unit: a unit price, shown with 4 decimals as 0.0030
        Assert.Equal(0.0030m, Money.RoundUnitPrice(line.UnitCost));
        Assert.Equal(3.00m, line.Amount);
        Assert.Equal(3.00m, Money.Amount(line.BaseQty, line.UnitCost)); // the stock value of 1,000 pieces at 0.0030
    }

    [Fact]
    public void ALineValuedAtTheAverage_TakesItsAmountFromTheBaseQuantity_AndShowsTheCostPerEnteredUnit()
    {
        var product = NewProduct(cartonFactor: 12);
        var line = StockDocumentLine.Create(product, DocumentType.Issue, 2m, CartonId, null, null); // 2 cartons = 24 pieces
        var doc = NewIssue(2, (product, 1m));
        doc.ReplaceLines([line]);

        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [product.Id] = 1.255m });

        Assert.Equal(30.12m, line.Amount);          // 24 x 1.255 = 30.12
        Assert.Equal(15.06m, line.EnteredUnitCost); // 1.255 x 12 per carton
        Assert.Equal(1.255m, line.UnitCost);
    }

    [Fact]
    public void ALineAboveTheLargestAmount_IsADomainError_NotADatabaseOverflow()
    {
        var ex = Assert.Throws<DomainException>(() => StockDocumentLine.Create(P(1), DocumentType.Receipt, 1_000_000_000m, null, 1_000_000m, null)); // 1e15

        Assert.Equal(Messages.LineAmountTooLarge, ex.Message);
    }

    [Fact]
    public void ASubCentCostOnAnIssue_IsNotJudged_BecauseItIsNotUsed()
    {
        var line = StockDocumentLine.Create(P(1), DocumentType.Issue, 1m, null, 1.234m, null);

        Assert.Equal(0m, line.UnitCost);
        Assert.Equal(0m, line.EnteredUnitCost);
    }

    private static decimal D(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

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
