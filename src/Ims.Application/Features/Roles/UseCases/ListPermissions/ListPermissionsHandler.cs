using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// The catalog, in the order of the role editor, without the permissions of modules that are off: they cannot be granted now, and a role that already holds one keeps it unseen
/// (see <see cref="ModulePermissions"/>). The catalog is code; only the switches are read.
/// </summary>
public class ListPermissionsHandler(IModuleSwitches modules) : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public async Task<IReadOnlyList<PermissionDto>> HandleAsync(ListPermissionsQuery q, CancellationToken ct)
    {
        var enabled = await modules.EnabledAsync(ct);
        return PermissionCatalog.All.Where(p => ModulePermissions.IsOffered(p.Code, enabled))
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.IsDangerous, p.AdminCore, p.Requires, p.Module)).ToList();
    }
}
