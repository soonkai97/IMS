using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>PS-03: an issue that has not posted is valued at the current average cost, so the amount an approver sees and the approval rules test is real.</summary>
public class IssueEstimateTests
{
    private static Product P(int id) => NewProduct(id, $"SKU-{id}");

    [Fact]
    public void ADraftIssue_StartsAtZero_AndIsValuedWhenEstimated()
    {
        var doc = NewIssue(2, (P(1), 4m), (P(2), 10m));
        Assert.Equal(0m, doc.TotalAmount);

        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 2.5m, [2] = 1m });

        Assert.Equal(4m * 2.5m + 10m * 1m, doc.TotalAmount);
    }

    [Fact]
    public void AProductWithoutABalance_IsValuedAtZero()
    {
        var doc = NewIssue(2, (P(1), 4m));

        doc.EstimateIssueCosts(new Dictionary<int, decimal>());

        Assert.Equal(0m, doc.TotalAmount);
    }

    [Fact]
    public void AReceipt_KeepsItsEnteredCost()
    {
        var doc = NewReceipt(1, (P(1), 4m, 3m));

        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 99m });

        Assert.Equal(12m, doc.TotalAmount);
    }

    [Fact]
    public void APostedIssue_KeepsItsBookedCost()
    {
        var doc = NewIssue(2, (P(1), 4m)).Submitted();
        var balances = Balances(NewBalance(1, 10m, 5m));
        NewEngine().Post(doc, balances, NewContext(doc));

        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 99m });

        Assert.Equal(20m, doc.TotalAmount);
    }

    [Fact]
    public void PostingBooksTheRealCostOverTheEstimate()
    {
        var doc = NewIssue(2, (P(1), 4m));
        doc.EstimateIssueCosts(new Dictionary<int, decimal> { [1] = 1m });
        doc.Submitted();
        var balances = Balances(NewBalance(1, 10m, 5m));

        NewEngine().Post(doc, balances, NewContext(doc));

        Assert.Equal(20m, doc.TotalAmount);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.Submitted, true)]
    [InlineData(DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.Posted, false)]
    [InlineData(DocumentStatus.Reversed, false)]
    [InlineData(DocumentStatus.Cancelled, false)]
    public void OnlyUnpostedDocumentsAreOpen(DocumentStatus status, bool open)
    {
        Assert.Equal(open, DocumentStatuses.IsOpen(status));
    }
}
