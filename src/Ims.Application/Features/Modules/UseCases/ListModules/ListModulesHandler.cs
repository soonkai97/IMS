using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>Every module of the catalog with its state. Open to any signed-in user: the web needs the list to show or hide parts of the screen.</summary>
public class ListModulesHandler(IModuleSwitches switches) : IQueryHandler<ListModulesQuery, IReadOnlyList<ModuleDto>>
{
    public async Task<IReadOnlyList<ModuleDto>> HandleAsync(ListModulesQuery q, CancellationToken ct)
    {
        var enabled = await switches.EnabledAsync(ct);
        return ModuleCatalog.All.Select(m => ModuleDto.From(m, enabled.Contains(m.Code))).ToList();
    }
}
