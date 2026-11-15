namespace Ims.Domain;

/// <summary>Why a module is not switched. The handler maps each member to the sentence and the status it always answered with.</summary>
public enum ModuleSwitchRefusalReason
{
    None,
    ReasonRequired,
    NeedsOtherModules,
    NeededByOtherModules,
    NotReady,
    HasOpenData,
}
