using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class AuthTokenIssuer(IRepository<RefreshToken> tokensRepo, ITokenService tokens, IClock clock, IIdMap<Warehouse> warehouseIds, IUnitOfWork uow) : IAuthTokenIssuer, IAppService
{
    public async Task<AuthResult> IssueAsync(User user, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user, out var expires);
        var refresh = tokens.CreateRefreshTokenValue();
        tokensRepo.Add(RefreshToken.Issue(user.Id, tokens.Hash(refresh), clock.UtcNow));
        await uow.SaveChangesAsync(ct);
        return new AuthResult(access, expires, refresh, await UserInfoMapper.ToInfoAsync(user, warehouseIds, ct));
    }
}
