namespace Ims.Domain;

/// <summary>What a request to put a module in a state comes to once the state it has now is known: nothing to do, or the flip that is asked.</summary>
public enum ModuleSwitchOutcome
{
    Unchanged,
    TurnOn,
    TurnOff,
}
