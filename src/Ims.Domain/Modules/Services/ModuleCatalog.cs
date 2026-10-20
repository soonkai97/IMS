namespace Ims.Domain;

/// <summary>
/// Every module the system knows. Like <see cref="PermissionCatalog"/> it is code: a module is added here when its slice ships, and a code is never renamed. A code that
/// is not listed is never on, whatever a stored row says, so a module that is not registered is off.
/// </summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        new(ModuleCodes.Accounting, "Accounting", "Tax-ready fields on products and partners, and an export file of posted documents for your accountant.")
    ];

    /// <summary>
    /// Modules the API refuses to switch ON (turning one off is always allowed) because their surface is not safe to use yet. <c>accounting</c> waits for PL1.8: while it is on, a
    /// product or partner save that does not send the four tax fields clears them, and the forms of today never send them (docs/pm/pilot-slice-1-plan.md risk 11). PL1.8 deletes this
    /// list together with <c>NOT_YET_SWITCHABLE_ON</c> in the web, in the same pull request that makes the forms always send the fields.
    /// </summary>
    public static readonly IReadOnlyList<string> NotYetSwitchableOn = [ModuleCodes.Accounting];

    /// <summary>The codes in catalog order.</summary>
    public static readonly IReadOnlyList<string> Codes = All.Select(m => m.Code).ToArray();

    // First entry wins on a duplicate code: the catalog tests report the duplicate instead of the type failing to initialise.
    private static readonly Dictionary<string, ModuleDefinition> ByCode = All.GroupBy(m => m.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);

    public static ModuleDefinition? Find(string code) => ByCode.GetValueOrDefault(code);
}
