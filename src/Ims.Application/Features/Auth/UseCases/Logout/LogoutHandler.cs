using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class LogoutHandler(IRepository<RefreshToken> tokensRepo, ITokenService tokens, IUnitOfWork uow, IClock clock, IAuditTrail audit, IIdMap<User> userIds)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutCommand cmd, CancellationToken ct)
    {
        var stored = await tokensRepo.FirstOrDefaultAsync(new RefreshTokenByHashSpec(tokens.Hash(cmd.Body.RefreshToken ?? "")), ct);
        if (stored is { RevokedAt: null })
        {
            stored.Revoke(clock.UtcNow);
            // The audit row names the user by public id, like every other row of the user; only a real sign-out pays this one look-up.
            audit.Record(AuditActions.Logout, AuditEntities.User, (await userIds.ToPublicAsync([stored.UserId], ct))[stored.UserId].ToString());
            await uow.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }
}
