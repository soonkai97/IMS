using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The rights of a signed-in user, one lookup per request (ADR roles-permissions 3.3, lean scope). The answer is kept for a short time and forgotten on
/// every access write on this instance, so a demotion or a deactivation applies on the next request here and within the time to live elsewhere.
/// </summary>
public interface IAccessSnapshots
{
    /// <summary>The user's snapshot, or null when the user is missing or inactive (no snapshot means no access). A failed load throws; it never allows.</summary>
    Task<AccessSnapshot?> GetAsync(int userId, CancellationToken ct);

    /// <summary>Drops one user's snapshot. Call it after the commit that changed the user's role, status or warehouses.</summary>
    void Forget(int userId);

    /// <summary>Drops every snapshot. Call it after the commit of a change that can reach many users (the permissions of a role).</summary>
    void ForgetAll();
}
