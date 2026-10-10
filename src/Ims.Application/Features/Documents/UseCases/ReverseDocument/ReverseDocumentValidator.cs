using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ReverseDocumentValidator : AbstractValidator<ReverseDocumentCommand>
{
    public ReverseDocumentValidator() => RuleFor(x => x.Data.Reason).NotEmpty().WithMessage(Messages.ReverseReasonRequired)
            .MaximumLength(Limits.RemarkMaxLength).WithMessage(Messages.ReasonTooLong);
}
