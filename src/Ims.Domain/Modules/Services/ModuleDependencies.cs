namespace Ims.Domain;

/// <summary>
/// The prerequisites of the modules (<see cref="ModuleDefinition.Requires"/>) as two pure questions over the set of modules that are on. The switch asks them under the exclusive
/// modules lock, with the state read from the stored rows, never from a cache.
/// </summary>
public static class ModuleDependencies
{
    /// <summary>The modules <paramref name="code"/> requires that are not on, in catalog order of the requirement.</summary>
    public static IReadOnlyList<ModuleDefinition> MissingPrerequisites(string code, IReadOnlySet<string> enabled) =>
        (ModuleCatalog.Find(code)?.Requires ?? []).Where(r => !enabled.Contains(r)).Select(r => ModuleCatalog.Find(r)).OfType<ModuleDefinition>().ToList();

    /// <summary>The modules that are on and require <paramref name="code"/>, in catalog order.</summary>
    public static IReadOnlyList<ModuleDefinition> EnabledDependents(string code, IReadOnlySet<string> enabled) =>
        ModuleCatalog.All.Where(m => enabled.Contains(m.Code) && m.Requires.Contains(code)).ToList();
}
