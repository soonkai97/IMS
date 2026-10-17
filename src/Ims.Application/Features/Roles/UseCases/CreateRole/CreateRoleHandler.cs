using Ims.Domain;
namespace Ims.Application.Features.Roles;

public class CreateRoleHandler(IRepository<Role> roles, IRoleReader reader, ICurrentUser current, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateRoleCommand, RoleDto>
{
    public async Task<RoleDto> HandleAsync(CreateRoleCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var name = r.Name.Trim();
        Role? source = null;
        if (r.CopyFromRoleId is { } sourceId)
            source = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(sourceId), ct) ?? throw new DomainException(Messages.RoleCopySourceMissing);
        var permissions = source?.Grants.Select(g => g.PermissionCode).ToList() ?? r.Permissions ?? [];

        // A copy gives what the source holds at this moment, so it needs the same check as a list typed by hand.
        RoleContainment.CheckGrantable(permissions, current);
        if (await roles.AnyAsync(new RoleNameTakenSpec(name), ct)) throw new ConflictException(Messages.RoleNameTaken(name));

        var role = Role.Create(await RoleCodes.NextFreeAsync(roles, name, ct), name, r.Description, isSystem: false, permissions);
        roles.Add(role);
        audit.Record(AuditActions.RoleCreated, AuditEntities.Role, role.PublicId.ToString(),
            after: new { role.Code, role.Name, Permissions = PermissionCatalog.InOrder(permissions), CopiedFrom = source?.Code });
        await uow.SaveChangesAsync(ct);
        return (await reader.FindAsync(role.PublicId, ct))!;
    }
}
