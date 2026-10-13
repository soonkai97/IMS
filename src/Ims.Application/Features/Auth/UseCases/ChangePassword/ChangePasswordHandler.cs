using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>
/// A signed-in user changes their own password. A wrong current password counts as a failed login (so a stolen token cannot be used
/// to guess it), and a successful change ends every session of the user.
/// </summary>
public class ChangePasswordHandler(IRepository<User> users, IRepository<RefreshToken> tokens, IPasswordHasher hasher, ICurrentUser current,
    IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> HandleAsync(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FirstOrDefaultAsync(new UserWithWarehousesSpec(current.UserId), ct) ?? throw new UnauthorizedException(Messages.SessionExpired);
        var now = clock.UtcNow;
        if (user.IsLockedOut(now)) throw new UnauthorizedException(Messages.InvalidCredentials);

        if (!hasher.Verify(user.PasswordHash, cmd.Body.CurrentPassword))
        {
            user.RegisterFailedLogin(now);
            await uow.SaveChangesAsync(ct);
            throw new ValidationException(Messages.CurrentPasswordWrong);
        }

        user.ChangePasswordHash(hasher.Hash(cmd.Body.NewPassword));
        user.RegisterSuccessfulLogin();
        foreach (var token in await tokens.ListAsync(new ActiveRefreshTokensOfUserSpec(user.Id, now), ct)) token.Revoke(now);
        audit.Record(AuditActions.PasswordChanged, AuditEntities.User, user.PublicId.ToString(), actor: user.Username);
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
