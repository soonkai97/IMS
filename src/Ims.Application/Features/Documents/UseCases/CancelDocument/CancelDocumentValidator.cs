using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class CancelDocumentValidator : AbstractValidator<CancelDocumentCommand>
{
    public CancelDocumentValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
