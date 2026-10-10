using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The request side of the cost rule: a sort key (an enum member tagged <see cref="VisibleWithAttribute"/>, for example sorting stock by value) is
/// refused with 400 when the caller lacks the permission, because the order of the rows would reveal what the value hides. Filters and sort keys
/// are read from the whole request object, so a new endpoint with a restricted sort key is covered without a validator of its own.
/// </summary>
public static class RequestGuard
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Walkable = new();

    public static void EnsureAllowed(object? request, Func<string, bool> has, string path = "")
    {
        if (request is null) return;
        var type = request.GetType();
        if (type.IsEnum)
        {
            if (type.GetField(request.ToString()!)?.GetCustomAttribute<VisibleWithAttribute>() is { } tag && !has(tag.Permission))
                throw new ValidationException(Messages.RestrictedSortKey(request.ToString()!),
                    new Dictionary<string, string[]> { [ErrorKeys.Normalize(path)] = [Messages.RestrictedSortKey(request.ToString()!)] });
            return;
        }
        if (request is string || type.IsPrimitive || request is IEnumerable || !type.Assembly.GetName().Name!.StartsWith(nameof(Ims), StringComparison.Ordinal)) return;

        foreach (var property in Walkable.GetOrAdd(type, FindWalkable))
            EnsureAllowed(property.GetValue(request), has, path.Length == 0 ? property.Name : $"{path}.{property.Name}");
    }

    private static PropertyInfo[] FindWalkable(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && (p.PropertyType.IsEnum || (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).IsEnum
            || (p.PropertyType.IsClass && p.PropertyType != typeof(string) && p.PropertyType.Assembly.GetName().Name!.StartsWith(nameof(Ims), StringComparison.Ordinal))))
        .ToArray();
}
