using FluentValidation;
namespace Ims.Application.Features.Documents;

public sealed class UpdateDocumentValidator : AbstractValidator<UpdateDocumentCommand>
{
    public UpdateDocumentValidator() => RuleFor(x => x.Data).SetValidator(new DocumentSaveRequestValidator());
}
