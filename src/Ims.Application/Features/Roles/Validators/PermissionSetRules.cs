using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// What a list of permission codes must be before a role may hold it: only codes of the catalog, matched exactly (a different case or a trailing space is another, unknown
/// code), each once, and closed under what the catalog says a permission needs. The two approval tiers need nothing from each other: the Owner holds only the large one.
/// A list longer than the catalog is refused whole, and an answer names at most <see cref="MaxCodesNamed"/> bad codes, each cut short, so a large body cannot come back larger.
/// </summary>
internal static class PermissionSetRules
{
    private const int MaxCodesNamed = 10;
    private const int MaxCodeShown = 64;

    public static IRuleBuilderOptionsConditions<T, IReadOnlyList<string>?> MustBeAValidPermissionSet<T>(this IRuleBuilder<T, IReadOnlyList<string>?> rule) =>
        rule.Custom((codes, context) =>
        {
            if (codes is null) return;
            if (codes.Count > PermissionCatalog.All.Count) { context.AddFailure(Messages.TooManyPermissions); return; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var bad = 0;
            foreach (var code in codes)
            {
                var message = code is null || !PermissionCatalog.IsKnown(code) ? Messages.UnknownPermission(Shown(code))
                    : seen.Add(code) ? null : Messages.DuplicatePermission(code);
                if (message is not null && ++bad <= MaxCodesNamed) context.AddFailure(message);
            }
            if (bad > MaxCodesNamed) context.AddFailure(Messages.MorePermissionsRefused(bad - MaxCodesNamed));
            if (bad == 0 && PermissionCatalog.FirstMissingRequirement(seen) is var (needing, required))
                context.AddFailure(Messages.PermissionNeedsAnother(needing, required));
        });

    private static string Shown(string? code) => code is null ? "" : code.Length <= MaxCodeShown ? code : code[..MaxCodeShown] + "...";
}
