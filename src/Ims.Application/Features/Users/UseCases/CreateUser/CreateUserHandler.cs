using Ims.Domain;
namespace Ims.Application.Features.Users;

public class CreateUserHandler(IRepository<User> users, IIdMap<Warehouse> warehouseIds, IPasswordHasher hasher, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> HandleAsync(CreateUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        if (await users.AnyAsync(new UserTakenSpec(r.Username, r.Email), ct)) throw new ConflictException(Messages.UserExists);
        var warehouses = await UserWarehouses.ResolveAsync(warehouseIds, r.WarehouseIds, ct);

        var user = User.Create(r.Username, r.Email, r.DisplayName, r.Role, hasher.Hash(r.Password!), warehouses, r.IsActive);
        users.Add(user);
        await uow.SaveChangesAsync(ct);
        audit.Record(AuditActions.UserCreated, AuditEntities.User, user.Id.ToString(), after: new { user.Username, user.Role });
        await uow.SaveChangesAsync(ct);
        return await UserMapping.ToDtoAsync(user, warehouseIds, ct);
    }
}
