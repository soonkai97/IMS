namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// The engine against the frozen reference (<see cref="ReferenceRunner"/>), which shares no code with it: the scripted scenarios and four hundred seeded random sequences must
/// give the same ledger rows, balances, line snapshots and refusals. The committed fixtures pin the answers; this proves the answers are the ones the rules say.
/// </summary>
public class ReferenceParityTests
{
    private const int Sequences = 400;

    [Fact]
    public void TheScriptedScenariosGiveTheSameAnswersOnTheEngineAndOnTheReference() =>
        AssertSame(OracleRuns.Scripted(EngineRunner.Run), OracleRuns.Scripted(ReferenceRunner.Run));

    [Fact]
    public void TheSeededSequencesGiveTheSameAnswersOnTheEngineAndOnTheReference() =>
        AssertSame(OracleRuns.Sequences(Enumerable.Range(1, Sequences), EngineRunner.Run), OracleRuns.Sequences(Enumerable.Range(1, Sequences), ReferenceRunner.Run));

    [Fact]
    public void TheSequencesReachTheHardCasesSoTheComparisonMeansSomething()
    {
        var runs = OracleRuns.Sequences(Enumerable.Range(1, Sequences), EngineRunner.Run);
        var outcomes = runs.SelectMany(r => r.Traces).Select(t => t.Outcome).ToList();

        Assert.True(outcomes.Count(o => o == StepTrace.Ok) > Sequences * 10, "most steps post");
        Assert.True(outcomes.Count(o => o.StartsWith(nameof(InsufficientStockException))) > 100, "short stock is refused often");
        Assert.True(outcomes.Count(o => o.StartsWith(nameof(ReversalBlockedException))) > 20, "reversals run into stock that already left");
        Assert.True(runs.SelectMany(r => r.Traces).Count(t => t.OverrideUsed) > 50, "negative-stock overrides are used");
        Assert.True(runs.SelectMany(r => r.Traces).SelectMany(t => t.Ledger).Count(e => e.Reason == nameof(LedgerReason.Reversal)) > 200, "reversals write rows");
    }

    private static void AssertSame(IReadOnlyList<OracleRun> engine, IReadOnlyList<OracleRun> reference)
    {
        var differences = new List<string>();
        foreach (var file in StockOracleFiles.Scripted)
            differences.AddRange(StockOracleFile.Compare(file.File, file.Lines(engine), file.Lines(reference)));

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ (expected = engine, got = reference):\n" + string.Join('\n', differences.Take(15)));
    }
}
