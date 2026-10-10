namespace Ims.Domain;

/// <summary>Customer-configurable behaviour the domain rules depend on (BR-03 negative stock, BR-07 self-approval).</summary>
public record SystemSettings(bool AllowNegativeStock, bool AllowSelfApproval, int LowStockAlertCooldownHours);
