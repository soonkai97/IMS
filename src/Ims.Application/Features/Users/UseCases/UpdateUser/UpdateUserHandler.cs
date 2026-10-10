using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UpdateUserHandler(IRepository<User> users, IRepository<RefreshToken> tokens, IPasswordHasher hasher, ICurrentUser current,
    IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UpdateUserCommand, UserDto>
{
    public async Task<UserDto> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var user = await users.FirstOrDefaultAsync(new Auth.UserWithWarehousesSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.UserNotFound);
        if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email, cmd.Id), ct)) throw new ConflictException(Messages.UserExists);

        // Nobody may lock the system out of administration: not themselves, and not the last active administrator.
        var losesAdmin = user.Role == Roles.Admin && user.IsActive && (!r.IsActive || r.Role != Roles.Admin);
        if (losesAdmin)
        {
            if (user.Id == current.UserId) throw new ConflictException(Messages.CannotLockOutSelf);
            if (!await users.AnyAsync(new ActiveAdminsSpec(user.Id), ct)) throw new ConflictException(Messages.LastAdminRequired);
        }

        var wanted = (r.WarehouseIds ?? []).Distinct().ToList();
        var before = new { user.Username, user.Role, user.IsActive, Warehouses = user.Warehouses.Select(w => w.WarehouseId).ToList() };
        var accessChanged = user.Role != r.Role || user.IsActive != r.IsActive || !string.IsNullOrEmpty(r.Password) ||
                            !before.Warehouses.Order().SequenceEqual(wanted.Order());

        user.UpdateProfile(r.Username, r.Email, r.DisplayName, r.Role, r.IsActive);
        if (!string.IsNullOrEmpty(r.Password)) user.ChangePasswordHash(hasher.Hash(r.Password));
        user.AssignWarehouses(wanted);

        // A changed role, scope, status or password must not leave older sessions alive: end every refresh token so the
        // old access token expires within AccessTokenMinutes and cannot be renewed.
        if (accessChanged)
        {
            var now = clock.UtcNow;
            foreach (var token in await tokens.ListAsync(new ActiveRefreshTokensOfUserSpec(user.Id, now), ct)) token.Revoke(now);
        }

        audit.Record(AuditActions.UserUpdated, AuditEntities.User, cmd.Id.ToString(), before, new { user.Username, user.Role, user.IsActive, Warehouses = wanted });
        await uow.SaveChangesAsync(ct);
        return UserMapping.ToDto(user);
    }
}
