using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Creates a role from a list of permissions or as a copy of another. A list that names a permission of a module that is off is refused (400); a copy takes what the source shows,
/// that is the permissions offered now, so a role never starts with a grant the editor cannot see (the source keeps its own hidden grants).
/// </summary>
public class CreateRoleHandler(IRepository<Role> roles, IRoleReader reader, ICurrentUser current, IModuleSwitches modules, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateRoleCommand, RoleDto>
{
    public async Task<RoleDto> HandleAsync(CreateRoleCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var name = r.Name.Trim();
        var enabled = await modules.EnabledAsync(ct);
        Role? source = null;
        if (r.CopyFromRoleId is { } sourceId)
            source = await roles.FirstOrDefaultAsync(new RoleForUpdateSpec(sourceId), ct) ?? throw new DomainException(Messages.RoleCopySourceMissing);
        var typed = r.Permissions ?? [];
        var permissions = source is null ? typed : ModulePermissions.Offered(source.Grants.Select(g => g.PermissionCode), enabled);

        // Same order as the permission replace: what the caller may grant first (a copy gives what the source shows now, so it needs the same check as a list typed by hand), then what is offered.
        RoleContainment.CheckGrantable(permissions, current);
        if (ModulePermissions.NotOffered(typed, enabled).FirstOrDefault() is { } notOffered) throw new DomainException(Messages.PermissionNotAvailable(notOffered));
        if (await roles.AnyAsync(new RoleNameTakenSpec(name), ct)) throw new ConflictException(Messages.RoleNameTaken(name));

        var role = Role.Create(await RoleCodes.NextFreeAsync(roles, name, ct), name, r.Description, isSystem: false, permissions);
        roles.Add(role);
        audit.Record(AuditActions.RoleCreated, AuditEntities.Role, role.PublicId.ToString(),
            after: new { role.Code, role.Name, Permissions = PermissionCatalog.InOrder(permissions), CopiedFrom = source?.Code });
        await uow.SaveChangesAsync(ct);
        return (await reader.FindAsync(role.PublicId, ct))!;
    }
}
