using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public static class ReasonRuleExtensions
{
    /// <summary>
    /// A mandatory reason (reject, reverse, negative-stock override): present, at least <see cref="Limits.ReasonMinLength"/> characters once
    /// trimmed (the audit trail is the control, so "x" is not a reason) and not longer than a remark. The domain enforces the same rules.
    /// A blank value is reported once, as "required", not also as "too short".
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeAReason<T>(this IRuleBuilder<T, string?> rule, string requiredMessage) =>
        rule.NotEmpty().WithMessage(requiredMessage)
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length >= Limits.ReasonMinLength).WithMessage(Messages.ReasonTooShort)
            .MaximumLength(Limits.RemarkMaxLength).WithMessage(Messages.ReasonTooLong);
}
