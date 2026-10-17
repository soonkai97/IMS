namespace Ims.Application;

/// <summary>The permissions of the caller's token (an unauthenticated caller has none).</summary>
public class CallerPermissions(ICurrentUser current) : ICallerPermissions, IAppService
{
    public bool Has(string permission) => current.Rights().Has(permission);
}
