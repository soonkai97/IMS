using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class ApproveDocumentValidator : AbstractValidator<ApproveDocumentCommand>
{
    public ApproveDocumentValidator() => RuleFor(x => x.Data.NegativeStockOverrideReason).MaximumLength(Limits.RemarkMaxLength).WithMessage(Messages.ReasonTooLong);
}
