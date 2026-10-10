using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <summary>
/// Reads settings from the key/value table, once per request (the service is scoped): a use case that asks several times
/// (submit, post, the document reader) costs one query, not three. Other features depend on <see cref="ISystemSettings"/>, not on this class.
/// </summary>
public class DbSystemSettings(IReadRepository<SystemSetting> settings) : ISystemSettings
{
    private SystemSettings? _cached;

    public async Task<SystemSettings> GetAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        var s = (await settings.ListAsync(ct)).ToDictionary(x => x.Key, x => x.Value);
        return _cached = new SystemSettings(
            SettingValues.IsTrue(s.GetValueOrDefault(SettingKeys.AllowNegativeStock)),
            SettingValues.IsTrue(s.GetValueOrDefault(SettingKeys.AllowSelfApproval)),
            int.TryParse(s.GetValueOrDefault(SettingKeys.LowStockAlertCooldownHours), out var h) ? h : Limits.DefaultLowStockCooldownHours);
    }

    public void Invalidate() => _cached = null;
}
