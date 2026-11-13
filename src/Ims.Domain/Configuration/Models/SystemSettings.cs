namespace Ims.Domain;

/// <summary>
/// Customer-configurable behaviour the domain rules depend on (BR-03 negative stock, BR-07 self-approval).
/// <paramref name="EnabledModules"/> are the modules that are on, read with the same rows in the same statement (the settings table holds the switches too), so a use case that already asks for the
/// settings can ask whether a module is on without another query.
/// </summary>
public record SystemSettings(bool AllowNegativeStock, bool AllowSelfApproval, int LowStockAlertCooldownHours, IReadOnlySet<string>? EnabledModules = null)
{
    /// <summary>True while the module is on (a module without a row is off).</summary>
    public bool IsModuleOn(string code) => EnabledModules?.Contains(code) == true;
}
