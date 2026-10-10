namespace Ims.Application;

/// <summary>What the signed-in caller may see and do, by permission (never by role name).</summary>
public interface ICallerPermissions
{
    bool Has(string permission);
}
