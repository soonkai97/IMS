using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class CreateDocumentValidator : AbstractValidator<CreateDocumentCommand>
{
    public CreateDocumentValidator()
    {
        RuleFor(x => x.Type).Must(t => t is DocumentType.Receipt or DocumentType.Issue)
            .WithMessage(Messages.OnlyReceiptAndIssue);
        RuleFor(x => x.Data).SetValidator(new DocumentSaveRequestValidator());
    }
}
