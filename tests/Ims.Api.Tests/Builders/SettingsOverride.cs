using Ims.Api.Tests.Clients;
using Ims.Application.Features.Settings;

namespace Ims.Api.Tests.Builders;

/// <summary>
/// Changes global system settings for the duration of a <c>await using</c> block and restores the previous values afterwards.
/// Settings are global, which is safe here because test classes run one at a time.
/// </summary>
public sealed class SettingsOverride : IAsyncDisposable
{
    private readonly ApiClient _admin;
    private readonly SettingsDto _original;

    private SettingsOverride(ApiClient admin, SettingsDto original)
    {
        _admin = admin;
        _original = original;
    }

    public static async Task<SettingsOverride> ApplyAsync(ApiClient admin, bool? allowNegativeStock = null, bool? allowSelfApproval = null)
    {
        var original = (await admin.Settings.GetAsync()).Data<SettingsDto>();
        var changed = original with
        {
            AllowNegativeStock = allowNegativeStock ?? original.AllowNegativeStock,
            AllowSelfApproval = allowSelfApproval ?? original.AllowSelfApproval
        };
        (await admin.Settings.UpdateAsync(changed)).ShouldBeOk();
        return new SettingsOverride(admin, original);
    }

    public async ValueTask DisposeAsync() => (await _admin.Settings.UpdateAsync(_original)).ShouldBeOk();
}
