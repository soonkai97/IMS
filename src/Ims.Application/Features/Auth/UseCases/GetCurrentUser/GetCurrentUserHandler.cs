using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class GetCurrentUserHandler(IReadRepository<User> users, ICurrentUser current, IIdMap<Warehouse> warehouseIds, IAccessSnapshotLoader snapshotLoader) : IQueryHandler<GetCurrentUserQuery, UserInfo>
{
    public async Task<UserInfo> HandleAsync(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await users.FirstOrDefaultAsync(new UserWithWarehousesSpec(current.UserId, tracked: false), ct)
                   ?? throw new UnauthorizedException(Messages.SessionExpired);
        return await UserInfoMapper.ToInfoAsync(user, warehouseIds, snapshotLoader, ct);
    }
}
