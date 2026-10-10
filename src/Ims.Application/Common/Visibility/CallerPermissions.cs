using Ims.Domain;
namespace Ims.Application;

/// <summary>The permissions of the caller's role (an unauthenticated caller has none).</summary>
public class CallerPermissions(ICurrentUser current) : ICallerPermissions, IAppService
{
    public bool Has(string permission) => Permissions.Has(current.Role, permission);
}
