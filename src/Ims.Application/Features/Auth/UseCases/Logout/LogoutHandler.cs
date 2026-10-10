using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class LogoutHandler(IRepository<RefreshToken> tokensRepo, ITokenService tokens, IUnitOfWork uow, IClock clock, IAuditTrail audit)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> HandleAsync(LogoutCommand cmd, CancellationToken ct)
    {
        var stored = await tokensRepo.FirstOrDefaultAsync(new RefreshTokenByHashSpec(tokens.Hash(cmd.Body.RefreshToken ?? "")), ct);
        if (stored is { RevokedAt: null })
        {
            stored.Revoke(clock.UtcNow);
            audit.Record(AuditActions.Logout, AuditEntities.User, stored.UserId.ToString());
            await uow.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }
}
