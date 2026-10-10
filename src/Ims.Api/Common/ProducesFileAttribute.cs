namespace Ims.Api;

/// <summary>
/// Marks an action that answers 200 with a file (a report, a template, an attachment). The OpenAPI document then declares a binary
/// response body for it, so a generated client exposes a file/blob result instead of an untyped response.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProducesFileAttribute(params string[] contentTypes) : Attribute
{
    public const string AnyFile = "application/octet-stream";

    public IReadOnlyList<string> ContentTypes { get; } = contentTypes.Length > 0 ? contentTypes : [AnyFile];
}
