namespace Ims.Domain.Tests.Modules;

/// <summary>What a request to turn a module on or off comes to. The first refusal on the way decides, in the order the rules are asked.</summary>
public enum SwitchOutcome { Switched, Unchanged, ReasonRequired, NeedsOtherModules, NeededByOtherModules, NotReady, HasOpenData }

/// <summary>What the module's own settings row says before the request: no row, <c>"false"</c>, a value that is not exactly <c>"true"</c>, or <c>"true"</c>.</summary>
public enum OwnRow { Absent, Off, Junk, On }

/// <summary>
/// DDD-14: the decision of <c>SwitchModuleHandler</c> over every module x own row x which of the other modules are on x requested state x reason x readiness check x open-data check.
/// First asked of the handler (characterization of today's code), then of <c>ModuleSwitchPolicy</c>: one table, so both give the same answers. The expected outcome is written out here from the
/// sentences of the rule, independent of the code under test:
/// (1) a module whose row is not exactly "true" is off, and asking for the state it has is a 200 that decides nothing else (no reason needed, nothing read);
/// (2) otherwise a reason of at least five characters once trimmed is required (one sentence for "none" and for "too short");
/// (3) turning ON is refused while a module it requires is off (naming those, in the order the catalog lists the requirement), then while its readiness check refuses (that sentence);
/// (4) turning OFF is refused while a module that requires it is on (naming those, in catalog order), then while its open-data check counts blockers.
/// </summary>
public static class ModuleSwitchTable
{
    public static readonly string[] Codes = [ModuleCodes.Purchasing, ModuleCodes.Sales, ModuleCodes.Returns];

    /// <summary>None, spaces, one short of the minimum, and a good reason with spaces around it.</summary>
    public static readonly string?[] Reasons = [null, "   ", "abcd", "  Good reason  "];

    public const string ReadinessSentence = "Add an approval rule first.";

    public static string OpenDataClause(int count) => $"{count} things are still open";

    public const int OpenDataCount = 3;

    /// <param name="OthersOn">The other modules that are on. The module's own state comes from <paramref name="Row"/>.</param>
    /// <param name="NotReady">Its readiness check (asked when turning on) refuses.</param>
    /// <param name="OpenData">Its open-data check (asked when turning off) counts blockers.</param>
    public sealed record Case(string Code, OwnRow Row, IReadOnlyList<string> OthersOn, bool Wanted, string? Reason, bool NotReady, bool OpenData)
    {
        public bool Before => Row == OwnRow.On;

        /// <summary>The modules that are on, own state included.</summary>
        public IReadOnlyList<string> On => Before ? [.. OthersOn, Code] : OthersOn;

        public override string ToString() =>
            $"{Code} row {Row}, others on [{string.Join(",", OthersOn)}], {(Wanted ? "turn on" : "turn off")}, reason {(Reason is null ? "null" : $"'{Reason}'")}{(NotReady ? ", not ready" : "")}{(OpenData ? ", open data" : "")}";
    }

    public static bool ReasonIsGiven(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 5;

    /// <summary>The module's requirements that are off, in the order the catalog lists them.</summary>
    public static IReadOnlyList<string> MissingNames(Case c) =>
        ModuleCatalog.Find(c.Code)!.Requires.Where(r => !c.On.Contains(r)).Select(r => ModuleCatalog.Find(r)!.Name).ToList();

    /// <summary>The modules that require this one and are on, in catalog order.</summary>
    public static IReadOnlyList<string> DependentNames(Case c) =>
        ModuleCatalog.All.Where(m => m.Requires.Contains(c.Code) && c.On.Contains(m.Code)).Select(m => m.Name).ToList();

    public static string NameOf(string code) => ModuleCatalog.Find(code)!.Name;

    public static SwitchOutcome Expected(Case c)
    {
        if (c.Before == c.Wanted) return SwitchOutcome.Unchanged;
        if (!ReasonIsGiven(c.Reason)) return SwitchOutcome.ReasonRequired;
        if (c.Wanted)
        {
            if (MissingNames(c).Count > 0) return SwitchOutcome.NeedsOtherModules;
            return c.NotReady ? SwitchOutcome.NotReady : SwitchOutcome.Switched;
        }
        if (DependentNames(c).Count > 0) return SwitchOutcome.NeededByOtherModules;
        return c.OpenData ? SwitchOutcome.HasOpenData : SwitchOutcome.Switched;
    }

    /// <summary>The sentence of a refusal, as the API answers with it; null when nothing is refused.</summary>
    public static string? ExpectedMessage(Case c) => Expected(c) switch
    {
        SwitchOutcome.ReasonRequired => Messages.ModuleReasonRequired,
        SwitchOutcome.NeedsOtherModules => Messages.ModuleNeedsOthers(NameOf(c.Code), MissingNames(c)),
        SwitchOutcome.NeededByOtherModules => Messages.ModuleIsNeededBy(NameOf(c.Code), DependentNames(c)),
        SwitchOutcome.NotReady => ReadinessSentence,
        SwitchOutcome.HasOpenData => Messages.ModuleHasOpenData(NameOf(c.Code), OpenDataClause(OpenDataCount)),
        _ => null,
    };

    public static IEnumerable<Case> All() =>
        from code in Codes
        from row in Enum.GetValues<OwnRow>()
        from others in Subsets(Codes.Where(o => o != code).ToArray())
        from wanted in new[] { true, false }
        from reason in Reasons
        from notReady in new[] { false, true }
        from openData in new[] { false, true }
        select new Case(code, row, others, wanted, reason, notReady, openData);

    private static IEnumerable<IReadOnlyList<string>> Subsets(string[] items) =>
        Enumerable.Range(0, 1 << items.Length).Select(mask => (IReadOnlyList<string>)items.Where((_, i) => (mask & (1 << i)) != 0).ToList());
}
