namespace Ims.Application.Features.Settings;

public sealed record SettingsDto(bool AllowNegativeStock, bool AllowSelfApproval, int LowStockAlertCooldownHours);
