namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>Runs the scripted scenarios and the seeded sequences on one implementation (the engine runner or the frozen reference).</summary>
internal static class OracleRuns
{
    public static IReadOnlyList<OracleRun> Scripted(Func<IReadOnlyList<OracleStep>, IReadOnlyList<StepTrace>> runner) =>
        [.. OracleScenarios.All.Select(s => new OracleRun(s.Name, s.Steps, runner(s.Steps)))];

    public static IReadOnlyList<OracleRun> Sequences(IEnumerable<int> seeds, Func<IReadOnlyList<OracleStep>, IReadOnlyList<StepTrace>> runner) =>
        [.. seeds.Select(seed =>
        {
            var steps = RandomSequences.Generate(seed);
            return new OracleRun(RandomSequences.NameOf(seed), steps, runner(steps));
        })];

    public static IReadOnlyList<OracleRun> CommittedSequences(Func<IReadOnlyList<OracleStep>, IReadOnlyList<StepTrace>> runner) =>
        Sequences(Enumerable.Range(1, RandomSequences.CommittedSeeds), runner);
}
