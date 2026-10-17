using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UpdateUserHandler(IRepository<User> users, IReadRepository<Role> roles, IIdMap<Warehouse> warehouseIds, IRepository<RefreshToken> tokens, IPasswordHasher hasher,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit, IAccessSnapshots snapshots) : ICommandHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var user = await users.FirstOrDefaultAsync(new UserWithWarehousesByPublicIdSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.UserNotFound);
        if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email, user.Id), ct)) throw new ConflictException(Messages.UserExists);
        var wanted = await UserWarehouses.ResolveAsync(warehouseIds, r.WarehouseIds, ct);
        var role = await roles.FirstOrDefaultAsync(new RoleByCodeSpec(r.Role), ct) ?? throw new DomainException(Messages.UnknownRole);

        // Nobody may lock the system out of administration: not themselves, and not the last active administrator.
        var losesAdmin = user.Role == Roles.Admin && user.IsActive && (!r.IsActive || r.Role != Roles.Admin);
        if (losesAdmin)
        {
            if (user.Id == current.UserId) throw new ConflictException(Messages.CannotLockOutSelf);
            if (!await users.AnyAsync(new ActiveAdminsSpec(user.Id), ct)) throw new ConflictException(Messages.LastAdminRequired);
        }

        var before = new { user.Username, user.Role, user.IsActive, Warehouses = user.Warehouses.Select(w => w.WarehouseId).ToList() };
        var endSessions = (user.IsActive && !r.IsActive) || !string.IsNullOrEmpty(r.Password);

        user.UpdateProfile(r.Username, r.Email, r.DisplayName, role, r.IsActive);
        if (!string.IsNullOrEmpty(r.Password)) user.ChangePasswordHash(hasher.Hash(r.Password));
        user.AssignWarehouses(wanted);

        // A deactivation or a new password ends every session: the refresh tokens are revoked so nobody can renew. A changed role or scope does not: the next
        // request reads the new rights from the access snapshot, so nobody is signed out for being promoted or demoted (ADR roles-permissions 3.4).
        if (endSessions)
        {
            var now = clock.UtcNow;
            foreach (var token in await tokens.ListAsync(new ActiveRefreshTokensOfUserSpec(user.Id, now), ct)) token.Revoke(now);
        }

        audit.Record(AuditActions.UserUpdated, AuditEntities.User, user.PublicId.ToString(), before, new { user.Username, user.Role, user.IsActive, Warehouses = wanted });
        await uow.SaveChangesAsync(ct);
        snapshots.Forget(user.Id); // after the commit: the next request reads the new role, scope and status here at once
        return await UserMapping.ToDtoAsync(user, warehouseIds, ct);
    }
}
