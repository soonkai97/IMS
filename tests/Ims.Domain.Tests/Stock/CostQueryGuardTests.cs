using Ims.Application.Features.Documents;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09: the type tests in <c>IssueValuation</c> and <c>ReceiptCostDefaults</c> look like repeats of the ones in <c>StockDocument.EstimateIssueCosts</c> and <c>ApplyKnownReceiptCosts</c>, but they decide
/// something the domain cannot: whether a balance query is sent at all. Without them every receipt submitted would read the balances for a valuation it does not need, and every issue saved would read
/// them for known costs. These tests pin the number of queries per kind of document so the guards stay (PL1.0 freezes the counts on the wire; the hard constraint is no new query on a hot path).
/// </summary>
public class CostQueryGuardTests
{
    [Fact]
    public async Task ValuingADocument_ReadsTheBalancesForAnIssueWithLinesAndForNothingElse()
    {
        Assert.Equal(1, await ValuationQueriesAsync(NewIssue(1, (NewProduct(), 2m))));
        Assert.Equal(0, await ValuationQueriesAsync(NewReceipt(1, (NewProduct(), 2m, 1m))));
        Assert.Equal(0, await ValuationQueriesAsync(NewIssue(1)));                                    // an issue with no line has nothing to value
    }

    [Fact]
    public async Task KnownReceiptCosts_AreReadForAReceiptWithALineAtTheAverageAndForNothingElse()
    {
        var atAverage = StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 2m, null, null, null, costFromAverage: true);
        var receipt = NewReceipt(1);
        receipt.AddLines([atAverage]);
        Assert.Equal(1, await DefaultsQueriesAsync(receipt));
        Assert.Equal(0, await DefaultsQueriesAsync(NewReceipt(1, (NewProduct(), 2m, 1m))));          // every line has a typed cost
        Assert.Equal(0, await DefaultsQueriesAsync(NewIssue(1, (NewProduct(), 2m))));                // an issue line is always "at the average", and still no query
    }

    private static async Task<int> ValuationQueriesAsync(StockDocument doc)
    {
        var journal = new Journal();
        await new IssueValuation(new FakeRepository<StockBalance>([NewBalance(1, 5m, 2m)], journal)).EstimateAsync(doc, CancellationToken.None);
        return journal.Count;
    }

    private static async Task<int> DefaultsQueriesAsync(StockDocument doc)
    {
        var journal = new Journal();
        await new ReceiptCostDefaults(new FakeRepository<StockBalance>([NewBalance(1, 5m, 2m)], journal)).ApplyAsync(doc, CancellationToken.None);
        return journal.Count;
    }
}
