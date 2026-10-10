using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class PartnerSaveRequestValidator : AbstractValidator<PartnerSaveRequest>
{
    public PartnerSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage(Messages.PartnerNameRequired).MaximumLength(Limits.NameMaxLength).WithMessage(Messages.TextTooLong("Name", Limits.NameMaxLength));
        RuleFor(x => x.Type).IsInEnum();
    }
}
