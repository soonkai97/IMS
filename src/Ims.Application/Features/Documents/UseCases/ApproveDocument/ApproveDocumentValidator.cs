using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApproveDocumentValidator : AbstractValidator<ApproveDocumentCommand>
{
    /// <summary>The override reason is optional here (only a document that requires an override needs one, which the domain enforces), but a reason that is given must be a real one.</summary>
    public ApproveDocumentValidator()
    {
        RuleFor(x => x.Data.NegativeStockOverrideReason).MaximumLength(Limits.RemarkMaxLength).WithMessage(Messages.ReasonTooLong);
        RuleFor(x => x.Data.NegativeStockOverrideReason)
            .Must(reason => reason!.Trim().Length >= Limits.ReasonMinLength).WithMessage(Messages.ReasonTooShort)
            .When(x => !string.IsNullOrWhiteSpace(x.Data.NegativeStockOverrideReason));
    }
}
