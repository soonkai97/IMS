using System.Security.Cryptography;
using System.Text;
using static Ims.Domain.Tests.Stock.Oracle.OracleCsv;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// The five committed fixtures and what each column means. The four detail files hold the scripted scenarios; <see cref="Digests"/> holds a digest of every seeded random
/// sequence (a hash of everything the four detail files would hold for it, so a sequence of 40 steps costs one line).
/// </summary>
internal static class StockOracleFiles
{
    private const string Freeze = "Frozen answers of the posting engine on main at c89b32b (before lots, modules and any pilot change), written by OracleGeneratorTests. Never regenerate from changed code:";
    private const int DigestLength = 16;

    public static readonly StockOracleFile Outcomes = new(
        "outcomes.csv",
        [Freeze, "a changed answer is a change of behaviour that must be announced and reviewed.",
         "One row per step. outcome: Ok, or the exception type and message the engine raised. status: the document after the step (it keeps its status when a run is refused).",
         "overrideUsed: the posting run recorded a negative-stock override (BR-03)."],
        ["scenario", "step", "op", "doc", "outcome", "status", "overrideUsed"],
        run => run.Traces.Select((t, i) => new[] { run.Name, Step(i), run.Steps[i].Op, run.Steps[i].Doc, t.Outcome, t.Status, Flag(t.OverrideUsed) }));

    public static readonly StockOracleFile Ledger = new(
        "ledger.csv",
        [Freeze, "the ledger rows a step wrote, in the order written (BR-01). balanceAfter is the product's quantity right after the row; unitCost is per base unit."],
        ["scenario", "step", "row", "product", "qtyDelta", "balanceAfter", "unitCost", "reason"],
        run => run.Traces.SelectMany((t, i) => t.Ledger.Select((e, n) => new[]
            { run.Name, Step(i), (n + 1).ToString(), e.ProductId.ToString(), Number(e.QtyDelta), Number(e.BalanceAfter), Number(e.UnitCost), e.Reason })));

    public static readonly StockOracleFile Lines = new(
        "lines.csv",
        [Freeze, "the lines of the step's document after the step (BR-05): base quantity, cost per base unit, cost per entered unit, stored amount, and the receipt snapshots",
         "(avgBefore, avgAfter, previousLine) that a reversal reads. Line ids are numbered across the whole scenario in the order the lines were created."],
        ["scenario", "step", "line", "product", "baseQty", "unitCost", "enteredUnitCost", "amount", "avgBefore", "avgAfter", "previousLine"],
        run => run.Traces.SelectMany((t, i) => t.Lines.Select(l => new[]
            { run.Name, Step(i), l.LineId.ToString(), l.ProductId.ToString(), Number(l.BaseQty), Number(l.UnitCost), Number(l.EnteredUnitCost), Number(l.Amount),
              Number(l.AvgCostBefore), Number(l.AvgCostAfter), Number(l.PreviousLineId) })));

    public static readonly StockOracleFile Balances = new(
        "balances.csv",
        [Freeze, "every stock balance (one warehouse) after each step: quantity on hand, moving average cost, and the line that brought stock in last."],
        ["scenario", "step", "product", "qtyOnHand", "avgCost", "lastReceiptLine"],
        run => run.Traces.SelectMany((t, i) => t.Balances.Select(b => new[]
            { run.Name, Step(i), b.ProductId.ToString(), Number(b.QtyOnHand), Number(b.AvgCost), Number(b.LastReceiptLineId) })));

    public static readonly StockOracleFile Digests = new(
        "random.csv",
        [Freeze, "one row per seeded random sequence (see RandomSequences). failed: steps the engine refused. ledgerRows: rows written. balances: product:qty@avg of the final books.",
         "digest: the first 16 hex digits of SHA-256 over every row the four detail files would hold for the sequence."],
        ["seed", "steps", "failed", "ledgerRows", "balances", "digest"],
        run => [[run.Name, run.Steps.Count.ToString(), run.Traces.Count(t => t.Outcome != StepTrace.Ok).ToString(), run.Traces.Sum(t => t.Ledger.Count).ToString(),
                 string.Join(';', run.Traces[^1].Balances.Select(b => $"{b.ProductId}:{Number(b.QtyOnHand)}@{Number(b.AvgCost)}")), Digest(run)]]);

    /// <summary>The files that hold the scripted scenarios.</summary>
    public static readonly StockOracleFile[] Scripted = [Outcomes, Ledger, Lines, Balances];

    private static string Step(int index) => (index + 1).ToString();

    /// <summary>Everything a run holds in the four detail files, as one hash.</summary>
    public static string Digest(OracleRun run)
    {
        var text = string.Join('\n', Scripted.SelectMany(f => f.Rows(run).Select(OracleCsv.Row)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..DigestLength].ToLowerInvariant();
    }
}
