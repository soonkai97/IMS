using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UpdateUserHandler(IRepository<User> users, IReadRepository<Role> roles, IIdMap<Warehouse> warehouseIds, IRepository<RefreshToken> tokens, IPasswordHasher hasher,
    ICurrentUser current, IClock clock, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UpdateUserCommand, UserDto>
{
    public Task<UserDto> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        // The hash is slow on purpose: it is computed before the transaction, so it is never done while the roles lock is held (a queue of password saves would hold every role write up).
        var passwordHash = string.IsNullOrEmpty(cmd.Data.Password) ? null : hasher.Hash(cmd.Data.Password);
        return uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            // First, before anything is read: the roles lock serialises every write that can change who administers the system or which role a person holds, so what is read below is what
            // stays true until the commit (the role cannot be deleted or deactivated, and the other administrators cannot change, between this look-up and the save).
            await locker.LockRolesAsync(ct);
            var user = await users.FirstOrDefaultAsync(new UserWithWarehousesByPublicIdSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.UserNotFound);
            if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email, user.Id), ct)) throw new ConflictException(Messages.UserExists);
            var wanted = await UserWarehouses.ResolveAsync(warehouseIds, r.WarehouseIds, ct);
            var role = UserContainment.ExactRole(await roles.FirstOrDefaultAsync(new RoleForAssignmentSpec(r.Role), ct), r.Role);

            // Nobody gets more than they have: not a user who sees or holds more than the caller, not a role or warehouses beyond the caller's own.
            UserContainment.CheckTarget(user, current);
            UserContainment.CheckAssignment(role, wanted, current);

            // Nobody may lock the system out of administration: not themselves, and not the last active administrator (a person whose role holds the whole administration core, whatever the role is called).
            if (user.IsAdministrator && !(r.IsActive && role.Administers))
            {
                if (user.Id == current.UserId) throw new ConflictException(Messages.CannotLockOutSelf);
                await AdministrabilityGuard.EnsureAnotherAsync(users, new ActiveAdministratorsSpec(exceptUserId: user.Id), ct);
            }

            var before = new { user.Username, Role = user.Role.Code, user.AllWarehouses, user.IsActive, Warehouses = user.Warehouses.Select(w => w.WarehouseId).ToList() };
            var allWarehouses = UserWarehouses.ResolveAllWarehouses(r.AllWarehouses, user.AllWarehouses, current);
            var endSessions = (user.IsActive && !r.IsActive) || !string.IsNullOrEmpty(r.Password);

            user.UpdateProfile(r.Username, r.Email, r.DisplayName, role, r.IsActive);
            if (passwordHash is not null) user.ChangePasswordHash(passwordHash);
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
        }, ct);
    }
}
