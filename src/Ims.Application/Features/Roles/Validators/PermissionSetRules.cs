using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// What a list of permission codes must be before a role may hold it: only codes of the catalog, matched exactly (a different case or a trailing space is another, unknown
/// code), each once, and closed under what the catalog says a permission needs. The two approval tiers need nothing from each other: the Owner holds only the large one.
/// </summary>
internal static class PermissionSetRules
{
    public static IRuleBuilderOptionsConditions<T, IReadOnlyList<string>?> MustBeAValidPermissionSet<T>(this IRuleBuilder<T, IReadOnlyList<string>?> rule) =>
        rule.Custom((codes, context) =>
        {
            if (codes is null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var code in codes)
            {
                if (code is null || !PermissionCatalog.IsKnown(code)) context.AddFailure(Messages.UnknownPermission(code ?? ""));
                else if (!seen.Add(code)) context.AddFailure(Messages.DuplicatePermission(code));
            }
            if (seen.Count == codes.Count && PermissionCatalog.FirstMissingRequirement(seen) is var (needing, required))
                context.AddFailure(Messages.PermissionNeedsAnother(needing, required));
        });
}
