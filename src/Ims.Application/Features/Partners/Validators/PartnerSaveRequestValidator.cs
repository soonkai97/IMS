using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class PartnerSaveRequestValidator : AbstractValidator<PartnerSaveRequest>
{
    public PartnerSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Limits.NameMaxLength).WithMessage(Messages.PartnerNameRequired);
        RuleFor(x => x.Type).IsInEnum();
    }
}
