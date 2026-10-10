using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Support;

/// <summary>
/// One API endpoint, the permission that guards it, and a request for it.
/// <paramref name="OkWhenAllowed"/> is true when a valid request must succeed with 200; false when the outcome depends on data
/// (for example "approve document 999999" is 404 for those who may approve) and the test only checks that access was not denied.
/// </summary>
public sealed record EndpointCase(
    string Name, HttpMethod Method, string Permission, Func<ReferenceData, string> Path,
    Func<ReferenceData, object?>? Body = null, bool OkWhenAllowed = true)
{
    public override string ToString() => Name;
}
