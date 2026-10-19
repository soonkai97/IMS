namespace Ims.Domain;

public static class SettingKeys
{
    public const string AllowNegativeStock = "AllowNegativeStock";
    public const string AllowSelfApproval = "AllowSelfApproval";
    public const string LowStockAlertCooldownHours = "LowStockAlertCooldownHours";

    /// <summary>A module's switch is the row <c>Module.&lt;code&gt;</c> holding <see cref="SettingValues.True"/> or <see cref="SettingValues.False"/>; no row means off.</summary>
    public const string ModulePrefix = "Module.";

    public static string ForModule(string code) => ModulePrefix + code;
}
