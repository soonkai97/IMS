using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>The switch row of one module, tracked so that a flip can change it.</summary>
public sealed class ModuleSettingSpec : Specification<SystemSetting>
{
    public ModuleSettingSpec(string moduleCode)
    {
        var key = SettingKeys.ForModule(moduleCode);
        Query.Where(s => s.Key == key);
    }
}
