using FluentValidation;
namespace Ims.Application.Features.Partners;

public sealed class CreatePartnerValidator : AbstractValidator<CreatePartnerCommand>
{
    public CreatePartnerValidator() => RuleFor(x => x.Data).SetValidator(new PartnerSaveRequestValidator());
}
