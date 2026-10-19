using static Ims.Domain.Tests.Stock.Oracle.Steps;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// Seeded random sequences of receipts, issues and reversals over five products (two with a carton). The same seed always gives the same steps (<see cref="Random"/> with a seed is
/// stable across runs and machines), so a sequence can be replayed on the engine and on the frozen reference and its digest committed. Many of them run into refusals on purpose:
/// short stock, reversing what stock already left, reversing twice. This file is part of the fixtures: changing how a sequence is drawn changes every digest.
/// </summary>
internal static class RandomSequences
{
    public const int StepsPerSequence = 40;
    public const int CommittedSeeds = 60;

    private static readonly decimal[] Quantities = [1m, 1m, 2m, 3m, 5m, 8m, 12m, 0.5m, 2.25m, 40m];
    private static readonly decimal?[] Costs = [0.01m, 0.99m, 1m, 1.10m, 2.35m, 3.33m, 7.77m, 10m, 12.50m, 99.99m, null];

    public static string NameOf(int seed) => $"seed-{seed:D4}";

    public static IReadOnlyList<OracleStep> Generate(int seed, int length = StepsPerSequence)
    {
        var random = new Random(seed);
        var steps = new List<OracleStep>();
        var documents = new List<string>();

        for (var i = 1; i <= length; i++)
        {
            var roll = random.Next(100);
            if (roll < 15 && documents.Count > 0)
            {
                steps.Add(Reverse(documents[random.Next(documents.Count)]) with { AllowNegative = random.Next(4) == 0 });
                continue;
            }

            var isReceipt = roll < 55;
            var label = (isReceipt ? "R" : "I") + i;
            var lines = Enumerable.Range(0, random.Next(1, 4)).Select(_ => Line(random, isReceipt)).ToArray();
            documents.Add(label);
            steps.Add(isReceipt ? Receive(label, lines) : IssueStep(random, label, lines));
        }
        return steps;
    }

    /// <summary>An issue: mostly an ordinary one; sometimes it is allowed to go below zero, with or without an approver's reason, with or without a poster who may override.</summary>
    private static OracleStep IssueStep(Random random, string label, OracleLine[] lines)
    {
        var issue = Issue(label, lines);
        return random.Next(6) switch
        {
            0 => issue.Overridden(),
            1 => issue with { AllowNegative = true, CanOverride = random.Next(2) == 0, Approval = ApprovalState.Approved },
            2 => issue with { CanOverride = true, Approval = ApprovalState.Override, Reason = DefaultOverrideReason },
            _ => issue
        };
    }

    private static OracleLine Line(Random random, bool isReceipt)
    {
        var product = OracleProducts.All[random.Next(OracleProducts.All.Length)];
        var inCarton = OracleProducts.CartonFactor(product) is not null && random.Next(2) == 0;
        var qty = Quantities[random.Next(Quantities.Length)];
        return new OracleLine(product, qty, isReceipt ? Costs[random.Next(Costs.Length)] : null, inCarton ? TestData.CartonId : null);
    }
}
