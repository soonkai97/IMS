using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

public class CreateReturnValidator : AbstractValidator<CreateReturnCommand>
{
    public CreateReturnValidator() => RuleFor(x => x.Data.OriginalDocumentId).NotEqual(Guid.Empty).WithMessage(Messages.ReturnOriginalMissing);
}
