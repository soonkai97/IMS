using Ims.Domain;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Users;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Replaces the whole set of permissions of a role. The Administrator role is locked (it always holds every permission, so the system can always be administered). The caller must hold the
/// role's present permissions and every permission they ask for. A role that administers (it holds the whole administration core) cannot lose any of it when its holders are the last
/// administrators: the check runs under the roles lock, taken before anything is read, like the user save. The same set again is a 200 that changes nothing and writes no audit event.
/// A permission of a module that is off is refused (400); what the role already holds of such permissions is kept: the editor never saw it, so the saved set is the submitted one plus
/// those grants (<see cref="ModulePermissions.Merge"/>), they are neither added nor removed in the reply and the audit event, and the <c>Requires</c> closure is checked on the merged set.
/// </summary>
public class ReplaceRolePermissionsHandler(IRepository<Role> roles, IReadRepository<User> users, IRoleReader reader, IRoleVersion version, ICurrentUser current, ITransactionLocker locker,
    IModuleSwitches modules, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ReplaceRolePermissionsCommand, RolePermissionsResult>
{
    public Task<RolePermissionsResult> HandleAsync(ReplaceRolePermissionsCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var submitted = cmd.Data.Permissions;
        await locker.LockRolesAsync(ct);
        var role = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.RoleNotFound);
        await RoleContainment.CheckEditableAsync(role, users, current, ct);
        RoleContainment.CheckGrantable(submitted, current);
        version.EnsureCurrent(role, cmd.IfMatch);

        var enabled = await modules.EnabledAsync(ct);
        if (ModulePermissions.NotOffered(submitted, enabled).FirstOrDefault() is { } notOffered) throw new DomainException(Messages.PermissionNotAvailable(notOffered));
        var wanted = ModulePermissions.Merge(submitted, role.Grants.Select(g => g.PermissionCode), enabled);

        var administered = role.Administers;
        // The aggregate decides first what a system role allows (the Administrator role is locked), so that refusal is not hidden by the checks below.
        var (added, removed) = role.SetPermissions(wanted);
        // The closure is checked on the merged set: a hidden grant whose required permission the editor just took away is refused, naming both. Nothing is saved when it throws.
        if (PermissionCatalog.FirstMissingRequirement(wanted) is var (needing, required)) throw new DomainException(Messages.PermissionNeedsAnother(needing, required));
        if (administered && !Role.HoldsAdminCore(wanted))
            await AdministrabilityGuard.EnsureAnotherAsync(users, new ActiveAdministratorsSpec(exceptRoleId: role.Id), ct);
        if (added.Count > 0 || removed.Count > 0)
        {
            audit.Record(AuditActions.RolePermissionsChanged, AuditEntities.Role, role.PublicId.ToString(), new { role.Code, Removed = removed }, new { role.Code, Added = added });
            await uow.SaveChangesAsync(ct);
        }
        return new RolePermissionsResult((await reader.FindAsync(role.PublicId, ct))!, added, removed);
    }, ct);
}
