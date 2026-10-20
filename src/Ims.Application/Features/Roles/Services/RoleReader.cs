using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Reads roles for the screens. A role lists the permissions it holds that are offered now: the grants of a module that is off stay in the database and come back when the module
/// is on, but they are not shown, so the editor never shows a row it cannot save (see <see cref="ModulePermissions"/>).
/// </summary>
public sealed class RoleReader(IReadRepository<Role> roles, IRoleUsageReader usage, IRoleVersion version, IModuleSwitches modules) : IRoleReader, IAppService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(RoleFilter filter, CancellationToken ct) => await ToDtosAsync(await roles.ListAsync(new RolesListSpec(filter), ct), ct);

    public async Task<RoleDto?> FindAsync(Guid publicId, CancellationToken ct) =>
        (await ToDtosAsync(await roles.ListAsync(new RoleRowByPublicIdSpec(publicId), ct), ct)).FirstOrDefault();

    private async Task<IReadOnlyList<RoleDto>> ToDtosAsync(IReadOnlyList<RoleRow> rows, CancellationToken ct)
    {
        var users = await usage.CountUsersAsync(rows.Select(r => r.Id).ToList(), ct);
        var enabled = await modules.EnabledAsync(ct);
        return rows.Select(r => new RoleDto(r.PublicId, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, ModulePermissions.Offered(r.Permissions, enabled),
            users.GetValueOrDefault(r.Id), version.Token(r.PublicId, r.RowVersion))).ToList();
    }
}
