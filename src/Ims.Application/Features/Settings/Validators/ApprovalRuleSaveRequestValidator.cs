using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class ApprovalRuleSaveRequestValidator : AbstractValidator<ApprovalRuleSaveRequest>
{
    public ApprovalRuleSaveRequestValidator()
    {
        RuleFor(x => x).Must(x => x.AmountThreshold is not null || x.QtyThreshold is not null)
            .WithMessage(Messages.ThresholdRequired);
        RuleFor(x => x.AmountThreshold).GreaterThanOrEqualTo(0).When(x => x.AmountThreshold is not null);
        RuleFor(x => x.QtyThreshold).GreaterThanOrEqualTo(0).When(x => x.QtyThreshold is not null);
        RuleFor(x => x.RoleRequired).Must(r => Roles.All.Contains(r)).WithMessage(Messages.UnknownRole);
        RuleFor(x => x.DocType).IsInEnum();
    }
}
