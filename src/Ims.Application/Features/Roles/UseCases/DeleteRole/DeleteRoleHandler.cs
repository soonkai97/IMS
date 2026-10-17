using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Deletes (soft) a custom role that nobody holds. The count of users and the delete happen under the roles lock that the user save also takes, so a user cannot be given the role in
/// between and be left with a role that is gone (a user of a deleted role would vanish from the user list and could not sign in). The refusal says how many users hold the role, not who.
/// The grants stay as rows, hidden with the role: nothing reads a grant without its role.
/// </summary>
public class DeleteRoleHandler(IRepository<Role> roles, IReadRepository<User> users, IRoleUsageReader usage, ICurrentUser current, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<DeleteRoleCommand, Unit>
{
    public Task<Unit> HandleAsync(DeleteRoleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var role = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.RoleNotFound);
        if (role.IsSystem) throw new ConflictException(Messages.SystemRoleProtected);
        RoleContainment.CheckEditable(role, await users.FirstOrDefaultAsync(new RoleIdOfUserSpec(current.UserId), ct), current);

        await locker.LockRolesAsync(ct);
        if ((await usage.CountUsersAsync([role.Id], ct)).GetValueOrDefault(role.Id) is > 0 and var inUse) throw new ConflictException(Messages.RoleInUse(inUse));

        role.Delete();
        audit.Record(AuditActions.RoleDeleted, AuditEntities.Role, role.PublicId.ToString(), new { role.Code, role.Name, Permissions = PermissionCatalog.InOrder(role.Grants.Select(g => g.PermissionCode)) });
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }, ct);
}
