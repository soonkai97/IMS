using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// Reads the <c>Module.&lt;code&gt;</c> rows of the settings table (at most a handful) once per request; the service is scoped. It reads its own rows rather than sharing the
/// settings read, so asking for a switch costs one tiny query and touches nothing else. Other features depend on <see cref="IModuleSwitches"/>.
/// </summary>
public class ModuleSwitches(IReadRepository<SystemSetting> settings) : IModuleSwitches, IAppService
{
    private IReadOnlyList<string>? _enabled;

    public async Task<bool> IsEnabledAsync(string code, CancellationToken ct) => (await EnabledAsync(ct)).Contains(code, StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct)
    {
        if (_enabled is not null) return _enabled;
        var on = (await settings.ListAsync(new ModuleSettingsSpec(), ct)).Where(s => SettingValues.IsTrue(s.Value)).Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _enabled = ModuleCatalog.Codes.Where(code => on.Contains(SettingKeys.ForModule(code))).ToList();
    }

    public void Invalidate() => _enabled = null;
}
