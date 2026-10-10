namespace Ims.Application.Features.Settings;

public record SettingsDto(bool AllowNegativeStock, bool AllowSelfApproval, int LowStockAlertCooldownHours);
