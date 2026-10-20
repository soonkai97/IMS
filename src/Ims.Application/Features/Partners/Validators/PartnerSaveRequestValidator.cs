using FluentValidation;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class PartnerSaveRequestValidator : AbstractValidator<PartnerSaveRequest>
{
    public PartnerSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage(Messages.PartnerNameRequired).MaximumLength(Limits.NameMaxLength).WithMessage(Messages.TextTooLong("Name", Limits.NameMaxLength));
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Tin).HasTaxFieldShape(TaxFieldShapes.TinError);
        RuleFor(x => x.SstRegNo).HasTaxFieldShape(TaxFieldShapes.SstRegNoError);
    }
}
