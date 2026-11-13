using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class CancelDocumentValidator : AbstractValidator<CancelDocumentCommand>
{
    public CancelDocumentValidator() => RuleFor(x => x.Data.Reason).MaximumLength(Limits.RemarkMaxLength);
}
