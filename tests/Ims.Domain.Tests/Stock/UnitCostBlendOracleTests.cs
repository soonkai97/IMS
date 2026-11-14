using System.Globalization;
using Ims.Domain.Tests.Stock.Oracle;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-05: <see cref="UnitCost.Blend"/> is the average <see cref="StockBalance.Receive"/> produces, for every stock-in of the stock oracle (the frozen answers of the posting engine, <c>Stock/Oracle</c>), bit for bit.
/// The cases are read from the committed oracle files, not from the code under test: the quantity on hand before the stock-in, the average before, the base quantity and the cost that came in, and the average
/// the oracle recorded after. A copy of the old decimal expression is the second reference, over a seeded grid that reaches the branches the scripted scenarios do not.
/// </summary>
public class UnitCostBlendOracleTests
{
    private sealed record Case(string Name, decimal OnHand, decimal Average, decimal Qty, decimal Cost, decimal Expected)
    {
        public override string ToString() => $"{Name}: {OnHand} on hand at {Average} + {Qty} at {Cost} = {Expected}";
    }

    private static decimal Dec(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static bool SameBits(decimal a, decimal b) => decimal.GetBits(a).SequenceEqual(decimal.GetBits(b));

    private static string[][] Rows(StockOracleFile file) => [.. file.Committed().Skip(1).Select(l => l.Split(','))];

    /// <summary>Every stock-in of the scripted scenarios, with what the oracle recorded before and after it.</summary>
    private static IReadOnlyList<Case> OracleCases()
    {
        var ledger = Rows(StockOracleFiles.Ledger);     // scenario, step, row, product, qtyDelta, balanceAfter, unitCost, reason
        var lines = Rows(StockOracleFiles.Lines);       // scenario, step, line, product, baseQty, unitCost, enteredUnitCost, amount, avgBefore, avgAfter, previousLine
        var balances = Rows(StockOracleFiles.Balances); // scenario, step, product, qtyOnHand, avgCost, lastReceiptLine
        var cases = new List<Case>();

        foreach (var stockIn in ledger.Where(r => Dec(r[4]) > 0m))
        {
            var (scenario, step, product) = (stockIn[0], stockIn[1], stockIn[3]);
            var qty = Dec(stockIn[4]);
            var onHand = Dec(stockIn[5]) - qty;
            var cost = Dec(stockIn[6]);

            if (stockIn[7] == "Receipt")
            {
                // A receipt line carries the snapshot of the average around it. The nth stock-in of a product in a step is its nth receipt line that has one.
                var nth = ledger.Where(r => r[0] == scenario && r[1] == step && r[3] == product && r[7] == "Receipt").ToList().IndexOf(stockIn);
                var line = lines.Where(l => l[0] == scenario && l[1] == step && l[3] == product && l[8].Length > 0).ElementAt(nth);
                Assert.Equal(qty, Dec(line[4]));        // the join took the right line
                Assert.Equal(cost, Dec(line[5]));
                cases.Add(new Case($"{scenario} step {step} line {line[2]}", onHand, Dec(line[8]), qty, cost, Dec(line[9])));
            }
            else
            {
                // A reversed issue comes back at the average: the balance before and after the step are the recorded averages.
                var before = balances.SingleOrDefault(b => b[0] == scenario && b[1] == (int.Parse(step, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture) && b[2] == product);
                var after = balances.Single(b => b[0] == scenario && b[1] == step && b[2] == product);
                cases.Add(new Case($"{scenario} step {step} ({stockIn[7]})", onHand, before is null ? 0m : Dec(before[4]), qty, cost, Dec(after[4])));
            }
        }
        return cases;
    }

    /// <summary>A balance holding <paramref name="onHand"/> at <paramref name="average"/>, reached through the public methods.</summary>
    private static StockBalance BalanceAt(decimal onHand, decimal average)
    {
        var balance = StockBalance.OpenFor(1, 1);
        balance.Receive(1m, average);                                           // zero stock: the cost wins
        if (onHand > 1m) balance.Receive(onHand - 1m, average);                 // at the average: it stays
        else if (onHand < 1m) balance.Issue(1m - onHand, allowNegative: true);  // issues never change the average
        return balance;
    }

    /// <summary>The expression <c>StockBalance.Receive</c> held before DDD-05, copied as it was.</summary>
    private static decimal Before(decimal qtyOnHand, decimal avgCost, decimal qty, decimal unitCost)
    {
        var newQty = qtyOnHand + qty;
        return qtyOnHand <= 0 || newQty <= 0 ? unitCost
            : unitCost == avgCost ? avgCost
            : Math.Round((qtyOnHand * avgCost + qty * unitCost) / newQty, Limits.CostDecimals, MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void TheCases_AreMany_AndReachEveryBranchOfTheAverage()
    {
        var cases = OracleCases();

        Assert.True(cases.Count >= 60, $"only {cases.Count} stock-ins read from the oracle");
        Assert.Contains(cases, c => c.OnHand <= 0m);                                                   // into empty or negative stock: the new cost
        Assert.Contains(cases, c => c.OnHand > 0m && c.Cost == c.Average);                              // at the average: unchanged
        Assert.Contains(cases, c => c.OnHand > 0m && c.Cost != c.Average && c.Expected != c.Cost);     // a real blend
        Assert.Contains(cases, c => c.Expected.ToString(CultureInfo.InvariantCulture).Split('.').ElementAtOrDefault(1)?.Length == Limits.CostDecimals);   // an average that needed the tenth decimal
    }

    [Fact]
    public void Blend_GivesTheAverageTheOracleRecorded_ForEveryStockIn()
    {
        var wrong = new List<string>();
        foreach (var c in OracleCases())
        {
            var blended = UnitCost.Blend(Quantity.Signed(c.OnHand), UnitCost.Of(c.Average), Quantity.Positive(c.Qty), UnitCost.Of(c.Cost));
            if (blended.Value != c.Expected || OracleCsv.Number(blended.Value) != OracleCsv.Number(c.Expected)) wrong.Add($"{c} but Blend gave {blended.Value}");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Blend_EqualsTheAverageStockBalanceReceiveProduces_ForEveryStockIn()
    {
        var wrong = new List<string>();
        foreach (var c in OracleCases())
        {
            var balance = BalanceAt(c.OnHand, c.Average);
            var before = Before(balance.QtyOnHand, balance.AvgCost, c.Qty, c.Cost);
            balance.Receive(c.Qty, c.Cost);
            var blended = UnitCost.Blend(Quantity.Signed(c.OnHand), UnitCost.Of(c.Average), Quantity.Positive(c.Qty), UnitCost.Of(c.Cost));

            if (!SameBits(balance.AvgCost, blended.Value) || !SameBits(before, blended.Value)) wrong.Add($"{c}: Receive {balance.AvgCost}, old expression {before}, Blend {blended.Value}");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Blend_IsTheOldExpression_OverASeededGrid_ScaleIncluded()
    {
        var random = new Random(20261114);
        var wrong = new List<string>();
        for (var i = 0; i < 4000; i++)
        {
            var onHand = Math.Round((decimal)(random.NextDouble() * 120 - 20), 4);                              // -20 .. 100, four decimals
            var average = Math.Round((decimal)(random.NextDouble() * 50), random.Next(0, 11));
            var qty = Math.Max(0.0001m, Math.Round((decimal)(random.NextDouble() * 80), 4));
            var cost = random.Next(4) == 0 ? average : Math.Round((decimal)(random.NextDouble() * 50), random.Next(0, 11));   // one in four comes back at the average

            var expected = Before(onHand, average, qty, cost);
            var blended = UnitCost.Blend(Quantity.Signed(onHand), UnitCost.Of(average), Quantity.Positive(qty), UnitCost.Of(cost));

            if (!SameBits(expected, blended.Value)) wrong.Add($"{onHand} at {average} + {qty} at {cost}: {expected} against {blended.Value}");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void StockBalanceReceive_StillStoresADecimalAverage_AndAnEmptyBalanceTakesTheCost()
    {
        var balance = StockBalance.OpenFor(1, 1);

        balance.Receive(10m, 2m);
        balance.Receive(10m, 4m);

        Assert.Equal(3m, balance.AvgCost);
        Assert.IsType<decimal>(balance.AvgCost);
    }
}
