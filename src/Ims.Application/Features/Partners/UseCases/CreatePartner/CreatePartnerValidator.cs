using FluentValidation;
namespace Ims.Application.Features.Partners;

public class CreatePartnerValidator : AbstractValidator<CreatePartnerCommand>
{
    public CreatePartnerValidator() => RuleFor(x => x.Data).SetValidator(new PartnerSaveRequestValidator());
}
