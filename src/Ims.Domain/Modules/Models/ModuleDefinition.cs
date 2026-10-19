namespace Ims.Domain;

/// <summary>
/// One entry of the module catalog (<see cref="ModuleCatalog"/>): a part of the system that an installation switches on. The catalog is code; whether a module is on is data
/// (a <see cref="SystemSetting"/> row, <see cref="SettingKeys.ForModule"/>), and a module without a row is off.
/// </summary>
/// <param name="Code">Stable lower-case code, at most <see cref="Limits.ModuleCodeMaxLength"/> characters; the constant in <see cref="ModuleCodes"/> names it.</param>
/// <param name="Name">The name shown on the modules screen.</param>
/// <param name="Description">One plain sentence that says what turning the module on adds.</param>
public sealed class ModuleDefinition(string Code, string Name, string Description)
{
    public string Code { get; } = Code;
    public string Name { get; } = Name;
    public string Description { get; } = Description;

    /// <summary>Modules that must be on before this one can be turned on. Listed in the contract now; the first module with a dependency makes the switch enforce it.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];
}
