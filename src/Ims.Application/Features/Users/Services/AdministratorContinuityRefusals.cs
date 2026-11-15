using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// Maps the answer of <see cref="AdministratorContinuityPolicy"/> to the sentence and the status the API has always given (409, ADR roles-permissions 2.9, "administrability"). The caller holds the <c>roles</c> lock
/// (<see cref="ITransactionLocker.LockRolesAsync"/>) in the same transaction before it reads anything, which every write that can change who administers takes (the user save, the role permission replace; a role in use
/// cannot be deactivated or deleted), so two writes that each leave the other as the last one cannot both pass.
/// </summary>
internal static class AdministratorContinuityRefusals
{
    /// <exception cref="ConflictException">The write would lock the caller out, or leave no active administrator.</exception>
    public static void ThrowIfRefused(AdministratorContinuityRefusal refusal)
    {
        switch (refusal)
        {
            case AdministratorContinuityRefusal.None:
                return;
            case AdministratorContinuityRefusal.CannotLockOutSelf:
                throw new ConflictException(Messages.CannotLockOutSelf);
            case AdministratorContinuityRefusal.LastAdministrator:
                throw new ConflictException(Messages.LastAdminRequired);
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "An answer of the continuity policy that has no sentence: refuse, never allow.");
        }
    }
}
