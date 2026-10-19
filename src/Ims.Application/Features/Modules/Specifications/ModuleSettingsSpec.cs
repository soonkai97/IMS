using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>The switch rows of all modules: the settings whose key starts with <see cref="SettingKeys.ModulePrefix"/>.</summary>
public sealed class ModuleSettingsSpec : Specification<SystemSetting>
{
    public ModuleSettingsSpec() => Query.Where(s => s.Key.StartsWith(SettingKeys.ModulePrefix)).AsNoTracking();
}
