using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class AuthTokenIssuer(IRepository<RefreshToken> tokensRepo, ITokenService tokens, IClock clock, IIdMap<Warehouse> warehouseIds, IModuleSwitches modules, IUnitOfWork uow) : IAuthTokenIssuer, IAppService
{
    /// <summary>The caller loaded <paramref name="access"/> before anything was written, so a user with no live role never gets a refresh-token row.</summary>
    public async Task<AuthResult> IssueAsync(User user, AccessSnapshot access, CancellationToken ct)
    {
        var token = tokens.CreateAccessToken(user.Username, access, out var expires);
        var refresh = tokens.CreateRefreshTokenValue();
        tokensRepo.Add(RefreshToken.Issue(user.Id, tokens.Hash(refresh), clock.UtcNow));
        await uow.SaveChangesAsync(ct);
        return new AuthResult(token, expires, refresh,
            await UserInfoMapper.ToInfoAsync(user, access.RoleCode, access.RoleName, access.Permissions, access.AllWarehouses, access.WarehouseIds, warehouseIds, await modules.EnabledAsync(ct), ct));
    }
}
