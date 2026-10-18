using System.Text;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// The code of a new custom role, made from its name (a client never chooses it): the letters and digits of the words, each word capitalised, as the system roles are written
/// (<c>WarehouseStaff</c>). A code is unique among live roles whatever the case (the database compares so), so a taken one gets the next free number.
/// </summary>
internal static class RoleCodes
{
    private const string Fallback = "Role";

    public static string Base(string name)
    {
        var code = new StringBuilder();
        var startOfWord = true;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c)) { startOfWord = true; continue; }
            code.Append(startOfWord ? char.ToUpperInvariant(c) : c);
            startOfWord = false;
        }
        return code.Length == 0 ? Fallback : code.ToString()[..Math.Min(code.Length, Limits.RoleMaxLength)];
    }

    /// <summary>The base code, or the base with the smallest number from 2 up that no live role has (cut short so the whole stays within the limit).</summary>
    public static async Task<string> NextFreeAsync(IReadRepository<Role> roles, string name, CancellationToken ct)
    {
        var baseCode = Base(name);
        var taken = (await roles.ListAsync(new RoleCodesStartingWithSpec(baseCode[..Math.Min(baseCode.Length, Limits.RoleMaxLength - 4)]), ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseCode)) return baseCode;
        for (var n = 2; ; n++)
        {
            var suffix = n.ToString();
            var candidate = baseCode[..Math.Min(baseCode.Length, Limits.RoleMaxLength - suffix.Length)] + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
