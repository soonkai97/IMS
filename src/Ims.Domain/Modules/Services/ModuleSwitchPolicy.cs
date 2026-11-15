namespace Ims.Domain;

/// <summary>
/// The decision of a request to turn a module on or off, as pure questions over facts the caller has read (docs/architecture/ddd-roadmap.md 2.2, DDD-14). It reads nothing and locks nothing. The facts come from reads that
/// happen in a fixed order, under the <c>modules</c> lock, with nothing asked once a refusal is known (the stored row, the modules that are on, the module's checks, the roles that regain permissions), so the policy answers
/// one step at a time, each over exactly the facts the caller holds at that step, and a refusal stops the caller from reading the next. The caller maps each refusal to the sentence and the status it always answered with.
/// The order: <see cref="Outcome"/>; for a flip, <see cref="ReasonRefusal"/>, then <see cref="DependencyRefusal"/>, then, turning on, <see cref="ReadinessRefusal"/> of each check of the module, or, turning off,
/// <see cref="OpenDataRefusal"/> of each.
/// </summary>
public static class ModuleSwitchPolicy
{
    /// <summary>Whether the request changes anything: the state asked for is the state the module has (a row that is not exactly "true" is off), or a flip on or off.</summary>
    public static ModuleSwitchOutcome Outcome(bool enabledBefore, bool wanted) =>
        enabledBefore == wanted ? ModuleSwitchOutcome.Unchanged : wanted ? ModuleSwitchOutcome.TurnOn : ModuleSwitchOutcome.TurnOff;

    /// <summary>A flip must say why (BR-02): at least <see cref="Limits.ReasonMinLength"/> characters once trimmed. One refusal for "none given" and for "too short". An unchanged request needs no reason, so the caller asks only for a flip.</summary>
    public static ModuleSwitchRefusal ReasonRefusal(string? reason) =>
        new ReasonRequiredRule(reason, Messages.ModuleReasonRequired, Limits.ReasonMinLength).IsBroken() ? Refused(ModuleSwitchRefusalReason.ReasonRequired) : ModuleSwitchRefusal.None;

    /// <summary>
    /// The graph of <see cref="ModuleDefinition.Requires"/> over the modules that are on, read from the stored rows: turning on is refused while a module it requires is off, turning off while a module that requires it is on.
    /// The refusal names those modules in catalog order. An unchanged request has no graph to check.
    /// </summary>
    public static ModuleSwitchRefusal DependencyRefusal(ModuleDefinition module, ModuleSwitchOutcome outcome, IReadOnlySet<string> enabled) => outcome switch
    {
        ModuleSwitchOutcome.TurnOn when ModuleDependencies.MissingPrerequisites(module.Code, enabled) is { Count: > 0 } missing => new(ModuleSwitchRefusalReason.NeedsOtherModules, missing),
        ModuleSwitchOutcome.TurnOff when ModuleDependencies.EnabledDependents(module.Code, enabled) is { Count: > 0 } dependents => new(ModuleSwitchRefusalReason.NeededByOtherModules, dependents),
        _ => ModuleSwitchRefusal.None,
    };

    /// <summary>The answer of one readiness check of the module (turning on): a sentence is a refusal, whatever it says; none lets the switch go on.</summary>
    public static ModuleSwitchRefusal ReadinessRefusal(string? checkRefusal) => checkRefusal is null ? ModuleSwitchRefusal.None : Refused(ModuleSwitchRefusalReason.NotReady);

    /// <summary>The count of one open-data check of the module (turning off): any blocker is a refusal.</summary>
    public static ModuleSwitchRefusal OpenDataRefusal(int blockers) => blockers > 0 ? Refused(ModuleSwitchRefusalReason.HasOpenData) : ModuleSwitchRefusal.None;

    private static ModuleSwitchRefusal Refused(ModuleSwitchRefusalReason reason) => new(reason, []);
}
