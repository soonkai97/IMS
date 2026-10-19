namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>A named sequence of steps run against an empty stock book.</summary>
internal sealed record OracleScenario(string Name, IReadOnlyList<OracleStep> Steps);
