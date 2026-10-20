using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>The shape rules of the tax-ready fields as validator rules, so a request names the field (the rule itself lives in <see cref="TaxFieldShapes"/>). Blank passes: it means "none".</summary>
public static class TaxFieldRules
{
    public static IRuleBuilderOptionsConditions<T, string?> HasTaxFieldShape<T>(this IRuleBuilder<T, string?> rule, Func<string?, string?> error) =>
        rule.Custom((value, context) =>
        {
            if (error(TaxFieldShapes.Clean(value)) is { } message) context.AddFailure(message);
        });
}
