using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Support;

/// <summary>One Set-Cookie response header, split into name, value and attributes.</summary>
public sealed record SetCookie(string Name, string Value, IReadOnlyDictionary<string, string> Attributes)
{
    public static SetCookie? Find(ApiResponse response, string name)
    {
        foreach (var raw in response.HeaderValues("Set-Cookie"))
        {
            var parts = raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var pair = parts[0].Split('=', 2);
            if (pair[0] != name) continue;
            var attributes = parts.Skip(1).Select(p => p.Split('=', 2))
                .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.OrdinalIgnoreCase);
            return new SetCookie(pair[0], Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : ""), attributes);
        }
        return null;
    }

    public bool Has(string attribute) => Attributes.ContainsKey(attribute);

    public string? Attribute(string attribute) => Attributes.TryGetValue(attribute, out var value) ? value : null;

    /// <summary>The server asked the browser to drop the cookie (empty value and an expiry in the past).</summary>
    public bool IsDeletion => Value.Length == 0 && Attribute("expires") is { } expires && DateTimeOffset.Parse(expires) < DateTimeOffset.UtcNow;
}
