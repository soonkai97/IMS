using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// The shape rules of the tax-ready fields as validator rules, so a request names the field (the rule itself lives in <see cref="TaxFieldShapes"/>). Blank passes: it means "none".
/// A request holds the field as <see cref="Optional{T}"/>, so the rule is put on its <c>.Value</c> (null when the key was left out, which passes) and the failure is filed under the field's own name.
/// </summary>
public static class TaxFieldRules
{
    public static IRuleBuilderOptionsConditions<T, string?> HasTaxFieldShape<T>(this IRuleBuilder<T, string?> rule, string property, Func<string?, string?> error) =>
        rule.Custom((value, context) =>
        {
            if (error(TaxFieldShapes.Clean(value)) is { } message) context.AddFailure(property, message);
        });
}
