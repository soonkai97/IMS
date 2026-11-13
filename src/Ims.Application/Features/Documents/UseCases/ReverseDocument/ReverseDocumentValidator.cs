using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class ReverseDocumentValidator : AbstractValidator<ReverseDocumentCommand>
{
    public ReverseDocumentValidator() => RuleFor(x => x.Data.Reason).MustBeAReason(Messages.ReverseReasonRequired);
}
