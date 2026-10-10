using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The single rule for cost and value (see <see cref="VisibleWithAttribute"/>): a tagged value is <c>null</c> in the response of a caller who lacks the permission,
/// wherever it sits (nested objects, lists, pages). Run by <see cref="CostVisibilityBehavior"/> on the result of every request. Handlers see the full
/// values; only what leaves the Application layer is shaped, so a tagged property is nullable and settable (<c>init</c>).
/// Strings tagged <see cref="ScrubsRestrictedKeysAttribute"/> are JSON: the restricted keys are removed from them.
/// </summary>
public static class ResponseShaper
{
    private sealed record Hidden(PropertyInfo Property, string Permission, PropertyInfo? OnlyWhen);

    private sealed record Plan(Hidden[] Hidden, PropertyInfo[] Json, PropertyInfo[] Children);

    private static readonly ConcurrentDictionary<Type, Plan> Plans = new();

    /// <summary>Hides, in place, everything in <paramref name="response"/> that the caller may not see.</summary>
    public static void Shape(object? response, Func<string, bool> has) => Walk(response, has);

    private static void Walk(object? value, Func<string, bool> has)
    {
        if (value is null) return;
        var type = value.GetType();
        if (IsLeaf(type)) return;
        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence) Walk(item, has);
            return;
        }
        if (!type.Assembly.GetName().Name!.StartsWith(nameof(Ims), StringComparison.Ordinal)) return;

        var plan = Plans.GetOrAdd(type, Build);
        foreach (var hidden in plan.Hidden)
        {
            if (has(hidden.Permission)) continue;
            if (hidden.OnlyWhen is not null && hidden.OnlyWhen.GetValue(value) is not true) continue;
            hidden.Property.SetValue(value, null);
        }
        foreach (var json in plan.Json)
            json.SetValue(value, AuditPayloadScrubber.Scrub((string?)json.GetValue(value), has));
        foreach (var child in plan.Children) Walk(child.GetValue(value), has);
    }

    private static Plan Build(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead).ToList();
        var hidden = new List<Hidden>();
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute<VisibleWithAttribute>() is not { } tag) continue;
            if (!property.CanWrite || !IsNullable(property))
                throw new InvalidOperationException($"{type.Name}.{property.Name} is tagged {nameof(VisibleWithAttribute)}, so it must be nullable and settable to be withheld.");
            var onlyWhen = tag.OnlyWhen is null ? null : type.GetProperty(tag.OnlyWhen) ?? throw new InvalidOperationException($"{type.Name} has no property {tag.OnlyWhen}.");
            hidden.Add(new Hidden(property, tag.Permission, onlyWhen));
        }
        var json = properties.Where(p => p.GetCustomAttribute<ScrubsRestrictedKeysAttribute>() is not null && p.PropertyType == typeof(string) && p.CanWrite).ToArray();
        var children = properties.Where(p => !IsLeaf(p.PropertyType) && p.GetCustomAttribute<VisibleWithAttribute>() is null).ToArray();
        return new Plan(hidden.ToArray(), json, children);
    }

    internal static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType;

    private static bool IsLeaf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateOnly)
            || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(DateTimeOffset)) return true;
        return type.IsArray && IsLeaf(type.GetElementType()!);
    }
}
