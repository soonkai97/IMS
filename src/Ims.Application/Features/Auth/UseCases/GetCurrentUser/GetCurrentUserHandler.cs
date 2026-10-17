using Ims.Application.Features.Users;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>
/// The caller as the presented access token sees them: the role, the scope and the permissions come from its claims, so what the screen shows is what this token is
/// allowed to do, not what the database says since. Only the person's own details (name, public id) and the role's display name are read.
/// </summary>
public class GetCurrentUserHandler(IReadRepository<User> users, IReadRepository<Role> roles, ICurrentUser current, IIdMap<Warehouse> warehouseIds) : IQueryHandler<GetCurrentUserQuery, UserInfo>
{
    public async Task<UserInfo> HandleAsync(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await users.FirstOrDefaultAsync(new ByIdSpec<User>(current.UserId, tracked: false), ct)
                   ?? throw new UnauthorizedException(Messages.SessionExpired);
        var roleName = (await roles.FirstOrDefaultAsync(new RoleByCodeSpec(current.Role), ct))?.Name ?? current.Role;
        return await UserInfoMapper.ToInfoAsync(user, current.Role, roleName, current.AllWarehouses, current.WarehouseIds, warehouseIds, ct);
    }
}
