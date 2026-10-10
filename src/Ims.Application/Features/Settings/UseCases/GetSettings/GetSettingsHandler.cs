namespace Ims.Application.Features.Settings;

public class GetSettingsHandler(ISystemSettings settings) : IQueryHandler<GetSettingsQuery, SettingsDto>
{
    public async Task<SettingsDto> HandleAsync(GetSettingsQuery q, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        return new SettingsDto(s.AllowNegativeStock, s.AllowSelfApproval, s.LowStockAlertCooldownHours);
    }
}
