using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The system always keeps at least one active user whose role holds the whole administration core (ADR roles-permissions 2.9, "administrability"): a write that would take the last one away
/// is refused (409). It is a rule about permissions, not about a role name. The caller holds the <c>roles</c> lock (<see cref="ITransactionLocker.LockRolesAsync"/>) in the same transaction
/// before it reads anything, which every write that can change who administers takes (the user save, the role permission replace; a role in use cannot be deactivated or deleted), so two
/// writes that each leave the other as the last one cannot both pass.
/// </summary>
internal static class AdministrabilityGuard
{
    /// <exception cref="ConflictException">No other administrator would remain.</exception>
    public static async Task EnsureAnotherAsync(IReadRepository<User> users, ActiveAdministratorsSpec others, CancellationToken ct)
    {
        if (!await users.AnyAsync(others, ct)) throw new ConflictException(Messages.LastAdminRequired);
    }
}
