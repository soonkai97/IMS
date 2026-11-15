namespace Ims.Domain;

public static class ModuleSwitchPolicy
{
    public static ModuleSwitchOutcome Outcome(bool enabledBefore, bool wanted) => ModuleSwitchOutcome.Unchanged;

    public static ModuleSwitchRefusal ReasonRefusal(string? reason) => ModuleSwitchRefusal.None;

    public static ModuleSwitchRefusal DependencyRefusal(ModuleDefinition module, ModuleSwitchOutcome outcome, IReadOnlySet<string> enabled) => ModuleSwitchRefusal.None;

    public static ModuleSwitchRefusal ReadinessRefusal(string? checkRefusal) => ModuleSwitchRefusal.None;

    public static ModuleSwitchRefusal OpenDataRefusal(int blockers) => ModuleSwitchRefusal.None;
}
