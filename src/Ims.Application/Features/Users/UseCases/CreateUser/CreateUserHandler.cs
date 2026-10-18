using Ims.Domain;
namespace Ims.Application.Features.Users;

public class CreateUserHandler(IRepository<User> users, IReadRepository<Role> roles, IIdMap<Warehouse> warehouseIds, IPasswordHasher hasher, ICurrentUser current, ITransactionLocker locker,
    IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateUserCommand, UserDto>
{
    public Task<UserDto> HandleAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        // The hash is slow on purpose: it is computed before the transaction, so it is never done while the roles lock is held.
        var passwordHash = hasher.Hash(cmd.Data.Password!);
        return uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email), ct)) throw new ConflictException(Messages.UserExists);
            var warehouses = await UserWarehouses.ResolveAsync(warehouseIds, r.WarehouseIds, ct);
            // Under the roles lock, so the role cannot be deleted or deactivated between this look-up and the save.
            await locker.LockRolesAsync(ct);
            var role = UserContainment.ExactRole(await roles.FirstOrDefaultAsync(new RoleForAssignmentSpec(r.Role), ct), r.Role);
            var allWarehouses = UserWarehouses.ResolveAllWarehouses(r.AllWarehouses, false, current);
            UserContainment.CheckAssignment(role, warehouses, current);

            var user = User.Create(r.Username, r.Email, r.DisplayName, role, passwordHash, warehouses, r.IsActive, allWarehouses);
            users.Add(user);
            await uow.SaveChangesAsync(ct);
            audit.Record(AuditActions.UserCreated, AuditEntities.User, user.PublicId.ToString(), after: new { user.Username, Role = role.Code, user.AllWarehouses });
            await uow.SaveChangesAsync(ct);
            return await UserMapping.ToDtoAsync(user, warehouseIds, ct);
        }, ct);
    }
}
