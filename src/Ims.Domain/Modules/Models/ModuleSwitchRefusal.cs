namespace Ims.Domain;

/// <summary>A refusal, or <see cref="None"/>. <paramref name="Modules"/> names the modules in the way for <see cref="ModuleSwitchRefusalReason.NeedsOtherModules"/> and <see cref="ModuleSwitchRefusalReason.NeededByOtherModules"/>, in catalog order; it is empty otherwise.</summary>
public readonly record struct ModuleSwitchRefusal(ModuleSwitchRefusalReason Reason, IReadOnlyList<ModuleDefinition> Modules)
{
    public static ModuleSwitchRefusal None => new(ModuleSwitchRefusalReason.None, []);

    public bool IsRefused => Reason != ModuleSwitchRefusalReason.None;
}
