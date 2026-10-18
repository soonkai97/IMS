using Ims.Domain;
namespace Ims.Application.Features.Roles;

internal static class RoleTextRules
{
    /// <summary>The name as the role keeps it (trimmed) is 2 to 60 characters.</summary>
    public static bool IsAValidName(string? name) => (name ?? "").Trim().Length is >= Limits.RoleNameMinLength and <= Limits.RoleNameMaxLength;
}
