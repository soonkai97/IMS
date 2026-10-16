using Ims.Domain;
namespace Ims.Application;

public static class CurrentUserRightsExtensions
{
    /// <summary>
    /// What the signed-in caller may do, for the Domain policies. Until roles come from the database (RP-4a) these are the defaults of the caller's
    /// system role; this is the one place that changes then.
    /// </summary>
    public static ActorRights Rights(this ICurrentUser current) => SystemRoleDefaults.RightsFor(current.Role, current.UserId);
}
