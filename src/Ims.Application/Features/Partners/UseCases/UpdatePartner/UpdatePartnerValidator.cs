using FluentValidation;
namespace Ims.Application.Features.Partners;

public sealed class UpdatePartnerValidator : AbstractValidator<UpdatePartnerCommand>
{
    public UpdatePartnerValidator() => RuleFor(x => x.Data).SetValidator(new PartnerSaveRequestValidator());
}
