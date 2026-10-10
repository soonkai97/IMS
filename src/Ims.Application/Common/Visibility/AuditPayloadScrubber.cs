using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// Removes from an audit before/after JSON every key that names a cost or value the caller may not see. The restricted names are the entity
/// properties tagged <see cref="VisibleWithAttribute"/> plus the payload keys of <see cref="AuditFields"/>. A name that two entities tag with
/// different permissions (a line's unit cost and the ledger's) is withheld unless the caller holds all of them.
/// </summary>
public static class AuditPayloadScrubber
{
    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> Restricted = new(FindRestrictedNames);

    /// <summary>Restricted key name (case-insensitive) to the permissions that each unlock it.</summary>
    public static IReadOnlyDictionary<string, string[]> RestrictedNames => Restricted.Value;

    public static string? Scrub(string? json, Func<string, bool> has)
    {
        if (string.IsNullOrEmpty(json)) return json;
        if (RestrictedNames.Values.SelectMany(p => p).Distinct().All(has)) return json;
        try
        {
            var node = JsonNode.Parse(json);
            Remove(node, has);
            return node?.ToJsonString();
        }
        catch (JsonException)
        {
            return null; // not JSON we wrote: fail closed rather than risk showing a value
        }
    }

    private static void Remove(JsonNode? node, Func<string, bool> has)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (RestrictedNames.TryGetValue(key, out var permissions) && !permissions.All(has)) obj.Remove(key);
                    else Remove(obj[key], has);
                }
                break;
            case JsonArray array:
                foreach (var item in array) Remove(item, has);
                break;
        }
    }

    private static IReadOnlyDictionary<string, string[]> FindRestrictedNames()
    {
        var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string permission)
        {
            if (!names.TryGetValue(name, out var set)) names[name] = set = [];
            set.Add(permission);
        }

        foreach (var type in typeof(VisibleWithAttribute).Assembly.GetTypes().Where(t => !t.IsEnum))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.GetCustomAttribute<VisibleWithAttribute>() is { } tag) Add(property.Name, tag.Permission);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
                if (field.GetCustomAttribute<VisibleWithAttribute>() is { } tag && field.GetRawConstantValue() is string key) Add(key, tag.Permission);
        }
        return names.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }
}
