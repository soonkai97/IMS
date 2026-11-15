namespace Ims.Domain.Tests.Modules;

/// <summary>
/// DDD-14: <see cref="ModuleSwitchPolicy"/>, the decision of <c>SwitchModuleHandler</c> as a pure function of facts. The facts come from reads that happen in a fixed order with nothing asked once a refusal is
/// known (the stored row, the modules that are on, the checks), so the policy answers one step at a time, over exactly the facts the handler holds at that step. The truth table is
/// <see cref="ModuleSwitchTable"/>, the one the handler was characterized with (<c>SwitchModuleHandlerTests</c>): driving the steps in the handler's order must give its answer for every case.
/// </summary>
public class ModuleSwitchPolicyTests
{
    [Fact]
    public void EveryCaseOfTheTable_GivesTheAnswerThatTheTableWrites_WhenTheStepsAreAskedInTheHandlersOrder()
    {
        var wrong = new List<string>();
        var count = 0;
        foreach (var c in ModuleSwitchTable.All())
        {
            count++;
            var (outcome, refusal) = Decide(c);

            var expected = ModuleSwitchTable.Expected(c);
            if (outcome != expected) wrong.Add($"{c}: expected {expected}, got {outcome}");
            else if (expected == SwitchOutcome.NeedsOtherModules && !refusal.Modules.Select(m => m.Name).SequenceEqual(ModuleSwitchTable.MissingNames(c))) wrong.Add($"{c}: names {string.Join(",", refusal.Modules.Select(m => m.Name))}, expected {string.Join(",", ModuleSwitchTable.MissingNames(c))}");
            else if (expected == SwitchOutcome.NeededByOtherModules && !refusal.Modules.Select(m => m.Name).SequenceEqual(ModuleSwitchTable.DependentNames(c))) wrong.Add($"{c}: names {string.Join(",", refusal.Modules.Select(m => m.Name))}, expected {string.Join(",", ModuleSwitchTable.DependentNames(c))}");
            else if (expected is not (SwitchOutcome.NeedsOtherModules or SwitchOutcome.NeededByOtherModules) && refusal.Modules.Count > 0) wrong.Add($"{c}: a refusal that names modules where none is in the way");
        }

        Assert.True(count > 1000, $"the table is the whole grid ({count} cases)");
        Assert.Equal(Enum.GetValues<SwitchOutcome>().Length, ModuleSwitchTable.All().Select(ModuleSwitchTable.Expected).Distinct().Count());
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {count} differ:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
    }

    /// <summary>The steps of <c>SwitchModuleHandler</c>, each asked only if the one before did not refuse.</summary>
    private static (SwitchOutcome Outcome, ModuleSwitchRefusal Refusal) Decide(ModuleSwitchTable.Case c)
    {
        var module = ModuleCatalog.Find(c.Code)!;
        var outcome = ModuleSwitchPolicy.Outcome(c.Before, c.Wanted);
        if (outcome == ModuleSwitchOutcome.Unchanged) return (SwitchOutcome.Unchanged, ModuleSwitchRefusal.None);

        var refusal = ModuleSwitchPolicy.ReasonRefusal(c.Reason);
        if (refusal.IsRefused) return (Map(refusal), refusal);

        var enabled = c.On.ToHashSet(StringComparer.Ordinal);
        refusal = ModuleSwitchPolicy.DependencyRefusal(module, outcome, enabled);
        if (refusal.IsRefused) return (Map(refusal), refusal);

        refusal = outcome == ModuleSwitchOutcome.TurnOn
            ? ModuleSwitchPolicy.ReadinessRefusal(c.NotReady ? ModuleSwitchTable.ReadinessSentence : null)
            : ModuleSwitchPolicy.OpenDataRefusal(c.OpenData ? ModuleSwitchTable.OpenDataCount : 0);
        return refusal.IsRefused ? (Map(refusal), refusal) : (SwitchOutcome.Switched, refusal);
    }

    private static SwitchOutcome Map(ModuleSwitchRefusal refusal) => refusal.Reason switch
    {
        ModuleSwitchRefusalReason.ReasonRequired => SwitchOutcome.ReasonRequired,
        ModuleSwitchRefusalReason.NeedsOtherModules => SwitchOutcome.NeedsOtherModules,
        ModuleSwitchRefusalReason.NeededByOtherModules => SwitchOutcome.NeededByOtherModules,
        ModuleSwitchRefusalReason.NotReady => SwitchOutcome.NotReady,
        ModuleSwitchRefusalReason.HasOpenData => SwitchOutcome.HasOpenData,
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal.Reason, "Not a refusal."),
    };

    // ----- the outcome -----

    [Theory]
    [InlineData(false, false, ModuleSwitchOutcome.Unchanged)]
    [InlineData(true, true, ModuleSwitchOutcome.Unchanged)]
    [InlineData(false, true, ModuleSwitchOutcome.TurnOn)]
    [InlineData(true, false, ModuleSwitchOutcome.TurnOff)]
    public void TheOutcome_IsAFlipOnlyWhenTheStateAskedForIsNotTheStateTheModuleHas(bool before, bool wanted, ModuleSwitchOutcome expected) =>
        Assert.Equal(expected, ModuleSwitchPolicy.Outcome(before, wanted));

    // ----- the reason -----

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("     ", true)]
    [InlineData("abcd", true)]
    [InlineData("  abcd  ", true)]   // the minimum is counted once trimmed
    [InlineData("abcde", false)]
    [InlineData("  abcde  ", false)]
    [InlineData("a b c", false)]     // spaces inside count
    public void AReasonIsRefused_WhenNoneIsGivenOrWhenItIsShorterThanFiveCharactersOnceTrimmed(string? reason, bool refused)
    {
        var refusal = ModuleSwitchPolicy.ReasonRefusal(reason);

        Assert.Equal(refused, refusal.IsRefused);
        Assert.Equal(refused ? ModuleSwitchRefusalReason.ReasonRequired : ModuleSwitchRefusalReason.None, refusal.Reason);
        Assert.Empty(refusal.Modules);
    }

    // ----- the graph -----

    [Fact]
    public void TurningOnIsRefused_NamingTheRequiredModulesThatAreOff_InTheOrderTheCatalogListsThem()
    {
        var returns = ModuleCatalog.Find(ModuleCodes.Returns)!;

        var none = ModuleSwitchPolicy.DependencyRefusal(returns, ModuleSwitchOutcome.TurnOn, new HashSet<string>());
        var oneLeft = ModuleSwitchPolicy.DependencyRefusal(returns, ModuleSwitchOutcome.TurnOn, new HashSet<string> { ModuleCodes.Sales });
        var ready = ModuleSwitchPolicy.DependencyRefusal(returns, ModuleSwitchOutcome.TurnOn, new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Purchasing });

        Assert.Equal(ModuleSwitchRefusalReason.NeedsOtherModules, none.Reason);
        Assert.Equal(["Sales", "Purchasing"], none.Modules.Select(m => m.Name));
        Assert.Equal(["Purchasing"], oneLeft.Modules.Select(m => m.Name));
        Assert.False(ready.IsRefused);
    }

    [Fact]
    public void TurningOffIsRefused_NamingTheModulesThatRequireItAndAreOn()
    {
        var sales = ModuleCatalog.Find(ModuleCodes.Sales)!;

        var needed = ModuleSwitchPolicy.DependencyRefusal(sales, ModuleSwitchOutcome.TurnOff, new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Returns });
        var free = ModuleSwitchPolicy.DependencyRefusal(sales, ModuleSwitchOutcome.TurnOff, new HashSet<string> { ModuleCodes.Sales, ModuleCodes.Purchasing });

        Assert.Equal(ModuleSwitchRefusalReason.NeededByOtherModules, needed.Reason);
        Assert.Equal(["Returns"], needed.Modules.Select(m => m.Name));
        Assert.False(free.IsRefused);
    }

    [Fact]
    public void TheGraphIsNotAskedAboutAnUnchangedRequest()
    {
        var returns = ModuleCatalog.Find(ModuleCodes.Returns)!;

        Assert.False(ModuleSwitchPolicy.DependencyRefusal(returns, ModuleSwitchOutcome.Unchanged, new HashSet<string>()).IsRefused);
    }

    // ----- the check results -----

    [Theory]
    [InlineData(null, false)]
    [InlineData("Add an approval rule first.", true)]
    [InlineData("", true)]   // any sentence is a refusal, even an empty one: the check said no
    public void TheReadinessCheck_RefusesWithAnySentence_AndAllowsWithNone(string? sentence, bool refused)
    {
        var refusal = ModuleSwitchPolicy.ReadinessRefusal(sentence);

        Assert.Equal(refused ? ModuleSwitchRefusalReason.NotReady : ModuleSwitchRefusalReason.None, refusal.Reason);
        Assert.Empty(refusal.Modules);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(40, true)]
    public void TheOpenDataCheck_RefusesWhenItCountsAnyBlocker(int blockers, bool refused)
    {
        var refusal = ModuleSwitchPolicy.OpenDataRefusal(blockers);

        Assert.Equal(refused ? ModuleSwitchRefusalReason.HasOpenData : ModuleSwitchRefusalReason.None, refusal.Reason);
        Assert.Empty(refusal.Modules);
    }

    [Fact]
    public void NoRefusal_NamesNoModules_AndTheDefaultRefusalIsNone()
    {
        Assert.False(ModuleSwitchRefusal.None.IsRefused);
        Assert.Empty(ModuleSwitchRefusal.None.Modules);
        Assert.Equal(ModuleSwitchRefusalReason.None, default(ModuleSwitchRefusal).Reason);
    }
}
