using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UpdateUserHandler(IRepository<User> users, IReadRepository<Role> roles, IIdMap<Warehouse> warehouseIds, IRepository<RefreshToken> tokens, IPasswordHasher hasher,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var user = await users.FirstOrDefaultAsync(new UserWithWarehousesByPublicIdSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.UserNotFound);
        if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email, user.Id), ct)) throw new ConflictException(Messages.UserExists);
        var wanted = await UserWarehouses.ResolveAsync(warehouseIds, r.WarehouseIds, ct);
        var role = UserContainment.ExactRole(await roles.FirstOrDefaultAsync(new RoleForAssignmentSpec(r.Role), ct), r.Role);

        // Nobody gets more than they have: not a user who sees or holds more than the caller, not a role or warehouses beyond the caller's own.
        UserContainment.CheckTarget(user, current);
        UserContainment.CheckAssignment(role, wanted, current);

        // Nobody may lock the system out of administration: not themselves, and not the last active administrator.
        var losesAdmin = user.Role.Code == SystemRoleCodes.Admin && user.IsActive && (!r.IsActive || role.Code != SystemRoleCodes.Admin);
        if (losesAdmin)
        {
            if (user.Id == current.UserId) throw new ConflictException(Messages.CannotLockOutSelf);
            if (!await users.AnyAsync(new ActiveAdminsSpec(user.Id), ct)) throw new ConflictException(Messages.LastAdminRequired);
        }

        var before = new { user.Username, Role = user.Role.Code, user.AllWarehouses, user.IsActive, Warehouses = user.Warehouses.Select(w => w.WarehouseId).ToList() };
        var allWarehouses = UserWarehouses.ResolveAllWarehouses(r.AllWarehouses, user.AllWarehouses, current);
        var endSessions = (user.IsActive && !r.IsActive) || !string.IsNullOrEmpty(r.Password);

        user.UpdateProfile(r.Username, r.Email, r.DisplayName, role, r.IsActive);
        if (!string.IsNullOrEmpty(r.Password)) user.ChangePasswordHash(hasher.Hash(r.Password));
        user.SetAllWarehouses(allWarehouses);
        user.AssignWarehouses(wanted);

        // A deactivation or a new password ends every session: the refresh tokens are revoked so nobody can renew. A changed role or scope does not: the rights ride in
        // the access token, so they apply at the next refresh (within AccessTokenMinutes plus the clock skew), and nobody is signed out for being promoted or demoted.
        if (endSessions)
        {
            var now = clock.UtcNow;
            foreach (var token in await tokens.ListAsync(new ActiveRefreshTokensOfUserSpec(user.Id, now), ct)) token.Revoke(now);
        }

        audit.Record(AuditActions.UserUpdated, AuditEntities.User, user.PublicId.ToString(), before, new { user.Username, Role = role.Code, AllWarehouses = allWarehouses, user.IsActive, Warehouses = wanted });
        await uow.SaveChangesAsync(ct);
        return await UserMapping.ToDtoAsync(user, warehouseIds, ct);
    }
}
