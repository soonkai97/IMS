namespace Ims.Api;

public static class RequestTimeoutPolicies
{
    /// <summary>Exports and imports may legitimately run longer than a normal request.</summary>
    public const string Long = "long";
}
