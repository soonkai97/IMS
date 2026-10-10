using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class RejectDocumentValidator : AbstractValidator<RejectDocumentCommand>
{
    public RejectDocumentValidator() => RuleFor(x => x.Data.Reason).NotEmpty().WithMessage(Messages.RejectReasonRequired)
            .MaximumLength(Limits.RemarkMaxLength).WithMessage(Messages.ReasonTooLong);
}
