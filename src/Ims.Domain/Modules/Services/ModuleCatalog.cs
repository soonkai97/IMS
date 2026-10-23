namespace Ims.Domain;

/// <summary>
/// Every module the system knows. Like <see cref="PermissionCatalog"/> it is code: a module is added here when its slice ships, and a code is never renamed. A code that
/// is not listed is never on, whatever a stored row says, so a module that is not registered is off.
/// </summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        new(ModuleCodes.Purchasing, "Purchasing", "Purchase orders, receiving against them, and a reorder list."),
        new(ModuleCodes.Sales, "Sales", "Sales orders, delivery against them and price lists.")
    ];

    /// <summary>The codes in catalog order.</summary>
    public static readonly IReadOnlyList<string> Codes = All.Select(m => m.Code).ToArray();

    // First entry wins on a duplicate code: the catalog tests report the duplicate instead of the type failing to initialise.
    private static readonly Dictionary<string, ModuleDefinition> ByCode = All.GroupBy(m => m.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);

    public static ModuleDefinition? Find(string code) => ByCode.GetValueOrDefault(code);
}
