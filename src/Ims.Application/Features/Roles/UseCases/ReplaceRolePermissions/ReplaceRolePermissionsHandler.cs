using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Replaces the whole set of permissions of a role. The Administrator role is locked (it always holds every permission, so the system can always be administered). The caller must hold the
/// role's present permissions and every permission they ask for. The same set again is a 200 that changes nothing and writes no audit event.
/// </summary>
public class ReplaceRolePermissionsHandler(IRepository<Role> roles, IReadRepository<User> users, IRoleReader reader, IRoleVersion version, ICurrentUser current, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<ReplaceRolePermissionsCommand, RolePermissionsResult>
{
    public async Task<RolePermissionsResult> HandleAsync(ReplaceRolePermissionsCommand cmd, CancellationToken ct)
    {
        var wanted = cmd.Data.Permissions;
        var role = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.RoleNotFound);
        RoleContainment.CheckEditable(role, await users.FirstOrDefaultAsync(new RoleIdOfUserSpec(current.UserId), ct), current);
        RoleContainment.CheckGrantable(wanted, current);
        if (role.Code == SystemRoleCodes.Admin) throw new ConflictException(Messages.AdminRoleLocked);
        version.EnsureCurrent(role, cmd.IfMatch);

        var (added, removed) = role.SetPermissions(wanted);
        if (added.Count > 0 || removed.Count > 0)
        {
            audit.Record(AuditActions.RolePermissionsChanged, AuditEntities.Role, role.PublicId.ToString(), new { role.Code, Removed = removed }, new { role.Code, Added = added });
            await uow.SaveChangesAsync(ct);
        }
        return new RolePermissionsResult((await reader.FindAsync(role.PublicId, ct))!, added, removed);
    }
}
