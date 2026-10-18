using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Renames a role, changes its description, activates or deactivates it. A role in use is not deactivated: the count is taken under the roles lock that the user save also takes, so a
/// user cannot be given the role between the count and the change.
/// </summary>
public class UpdateRoleHandler(IRepository<Role> roles, IReadRepository<User> users, IRoleUsageReader usage, IRoleReader reader, IRoleVersion version, ICurrentUser current,
    ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UpdateRoleCommand, RoleDto>
{
    public Task<RoleDto> HandleAsync(UpdateRoleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var r = cmd.Data;
        var name = r.Name.Trim();
        var role = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.RoleNotFound);
        await RoleContainment.CheckEditableAsync(role, users, current, ct);
        version.EnsureCurrent(role, cmd.IfMatch);

        var before = new { role.Name, role.Description, role.IsActive };
        var wasActive = role.IsActive;
        var active = r.IsActive ?? wasActive;
        // The aggregate decides first what a system role allows, so that refusal is not hidden by the in-use count below.
        role.UpdateText(name, r.Description ?? role.Description);
        role.SetActive(active);
        if (await roles.AnyAsync(new RoleNameTakenSpec(name, role.Id), ct)) throw new ConflictException(Messages.RoleNameTaken(name));
        if (!active && wasActive)
        {
            await locker.LockRolesAsync(ct);
            if ((await usage.CountUsersAsync([role.Id], ct)).GetValueOrDefault(role.Id) is > 0 and var inUse) throw new ConflictException(Messages.RoleInUse(inUse));
        }

        audit.Record(AuditActions.RoleUpdated, AuditEntities.Role, role.PublicId.ToString(), before, new { role.Code, role.Name, role.Description, role.IsActive });
        await uow.SaveChangesAsync(ct);
        return (await reader.FindAsync(role.PublicId, ct))!;
    }, ct);
}
