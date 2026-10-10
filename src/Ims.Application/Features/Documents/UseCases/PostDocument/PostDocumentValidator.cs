using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class PostDocumentValidator : AbstractValidator<PostDocumentCommand>
{
    public PostDocumentValidator() => RuleFor(x => x.Data.NegativeStockOverrideReason).MaximumLength(Limits.RemarkMaxLength);
}
