using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class RejectDocumentValidator : AbstractValidator<RejectDocumentCommand>
{
    public RejectDocumentValidator() => RuleFor(x => x.Data.Reason).MustBeAReason(Messages.RejectReasonRequired);
}
