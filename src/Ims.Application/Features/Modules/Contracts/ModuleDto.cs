using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>A module and whether this installation has it on. <c>Requires</c> lists the codes of modules that must be on first (none yet).</summary>
public record ModuleDto(string Code, string Name, string Description, bool Enabled, IReadOnlyList<string> Requires)
{
    public static ModuleDto From(ModuleDefinition module, bool enabled) => new(module.Code, module.Name, module.Description, enabled, module.Requires);
}
