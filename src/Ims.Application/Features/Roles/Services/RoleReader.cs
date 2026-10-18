using Ims.Domain;
namespace Ims.Application.Features.Roles;

public sealed class RoleReader(IReadRepository<Role> roles, IRoleUsageReader usage, IRoleVersion version) : IRoleReader, IAppService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(RoleFilter filter, CancellationToken ct) => await ToDtosAsync(await roles.ListAsync(new RolesListSpec(filter), ct), ct);

    public async Task<RoleDto?> FindAsync(Guid publicId, CancellationToken ct) =>
        (await ToDtosAsync(await roles.ListAsync(new RoleRowByPublicIdSpec(publicId), ct), ct)).FirstOrDefault();

    private async Task<IReadOnlyList<RoleDto>> ToDtosAsync(IReadOnlyList<RoleRow> rows, CancellationToken ct)
    {
        var users = await usage.CountUsersAsync(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new RoleDto(r.PublicId, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, PermissionCatalog.InOrder(r.Permissions),
            users.GetValueOrDefault(r.Id), version.Token(r.PublicId, r.RowVersion))).ToList();
    }
}
