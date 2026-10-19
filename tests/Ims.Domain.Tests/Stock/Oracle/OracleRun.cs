namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>A scenario (or a seeded random sequence) together with the trace one implementation made of it.</summary>
internal sealed record OracleRun(string Name, IReadOnlyList<OracleStep> Steps, IReadOnlyList<StepTrace> Traces);
