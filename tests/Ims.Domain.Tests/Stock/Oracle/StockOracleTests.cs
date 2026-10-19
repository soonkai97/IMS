namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// PL1.0 (ADR modules-and-tracking-dimensions, rollout step 0): the posting engine is pinned to the oracle fixtures (<c>*.csv</c> next to this file), so the lot work and the
/// pilot slices (returns, stocktake) cannot move a ledger row, a balance, a line snapshot or a refusal of an untracked product unnoticed. The files were written once, at the
/// commit before any of that work, by <see cref="OracleGeneratorTests"/>; they are never regenerated from changed code. A failure here means behaviour changed: fix the code, or
/// announce the change in HANDOFFS.md and move the fixtures in a reviewed PR of their own.
/// </summary>
public class StockOracleTests
{
    public static TheoryData<string> ScriptedFiles() => new(StockOracleFiles.Scripted.Select(f => f.File));

    [Theory]
    [MemberData(nameof(ScriptedFiles))]
    public void TheEngineAnswersTheScriptedScenariosAsTheOracleSays(string file)
    {
        var fixture = StockOracleFiles.Scripted.Single(f => f.File == file);

        var differences = fixture.Differences(OracleRuns.Scripted(EngineRunner.Run));

        Assert.True(differences.Count == 0, Describe(differences));
    }

    [Fact]
    public void TheEngineAnswersTheSeededSequencesAsTheOracleSays()
    {
        var differences = StockOracleFiles.Digests.Differences(OracleRuns.CommittedSequences(EngineRunner.Run));

        Assert.True(differences.Count == 0, Describe(differences));
    }

    [Fact]
    public void TheFixturesCoverEveryScenarioStepAndSeed()
    {
        var steps = OracleScenarios.All.Sum(s => s.Steps.Count);

        Assert.Equal(steps, StockOracleFiles.Outcomes.Committed().Count - 1);
        Assert.Equal(OracleScenarios.All.Select(s => s.Name), StockOracleFiles.Outcomes.Committed().Skip(1).Select(l => l.Split(',')[0]).Distinct());
        Assert.Equal(Enumerable.Range(1, RandomSequences.CommittedSeeds).Select(RandomSequences.NameOf), StockOracleFiles.Digests.Committed().Skip(1).Select(l => l.Split(',')[0]));
        Assert.Equal(OracleScenarios.All.Count, OracleScenarios.All.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void TheFixturesAreNotTrivial_EveryKindOfAnswerIsInThem()
    {
        // Guards the guard: files that were empty, all "Ok", or the same row repeated would make every comparison pass.
        var outcomes = StockOracleFiles.Outcomes.Committed().Skip(1).ToList();                                // whole rows: a message may hold commas
        var ledger = StockOracleFiles.Ledger.Committed().Skip(1).Select(l => l.Split(',')[7]).ToHashSet();
        var digests = StockOracleFiles.Digests.Committed().Skip(1).Select(l => l.Split(',')).ToList();

        Assert.Contains(outcomes, o => o.Contains($",{StepTrace.Ok},"));
        Assert.Contains(outcomes, o => o.Contains($"{nameof(InsufficientStockException)}: "));
        Assert.Contains(outcomes, o => o.Contains($"{nameof(ReversalBlockedException)}: "));
        Assert.Contains(outcomes, o => o.Contains(Messages.NegativeNeedsSupervisor));
        Assert.Contains(outcomes, o => o.Contains(Messages.NegativeNeedsReason));
        Assert.Contains(outcomes, o => o.Contains(Messages.ApprovalRequiredToPost));
        Assert.Contains(outcomes, o => o.Contains(Messages.OnlyPostedCanReverse));
        Assert.Contains(outcomes, o => o.Contains(Messages.ConversionMissing));
        Assert.Equal(["Issue", "Receipt", "Reversal"], ledger.Order());
        Assert.Contains(StockOracleFiles.Outcomes.Committed(), l => l.EndsWith(",1"));                  // an override was used
        Assert.Equal(RandomSequences.CommittedSeeds, digests.Select(d => d[5]).Distinct().Count());     // every sequence has its own digest
        Assert.Contains(digests, d => int.Parse(d[2]) > 0);                                              // some sequences are refused...
        Assert.Contains(digests, d => int.Parse(d[2]) == 0 || int.Parse(d[2]) < int.Parse(d[1]));        // ...and some steps succeed
    }

    [Fact]
    public void ChangingOneNumberOfTheEngineIsReported()
    {
        // Guards the comparison itself: one balance that is off in the tenth decimal place (a changed cost rounding) must show up as exactly that row.
        var runs = OracleRuns.Scripted(EngineRunner.Run).ToList();
        var run = runs.First(r => r.Name == "weighted-average");
        var last = run.Traces[^1];
        var drifted = last with { Balances = [.. last.Balances.Select(b => b with { AvgCost = b.AvgCost + 0.0000000001m })] };
        runs[runs.IndexOf(run)] = run with { Traces = [.. run.Traces.Take(run.Traces.Count - 1), drifted] };

        var differences = StockOracleFiles.Balances.Differences(runs);

        var one = Assert.Single(differences);
        Assert.StartsWith("balances.csv line ", one);
        Assert.Contains("weighted-average,5,1", one);
    }

    private static string Describe(IReadOnlyList<string> differences) =>
        $"{differences.Count} answer(s) differ from the oracle:\n" + string.Join('\n', differences.Take(15));
}
