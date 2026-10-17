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
        RuleFor(x => x.AmountThreshold).Must(a => Money.IsExact(a!.Value)).When(x => x.AmountThreshold is not null).WithMessage(Messages.AmountTooPrecise);
        RuleFor(x => x.QtyThreshold).GreaterThanOrEqualTo(0).When(x => x.QtyThreshold is not null);
        RuleFor(x => x.ApproverTier).Must(t => Enum.IsDefined(t!.Value)).When(x => x.ApproverTier is not null).WithMessage(Messages.UnknownApproverTier);
        RuleFor(x => x).Must(x => ApproverTierNames.Resolve(x.RoleRequired, x.ApproverTier) is not null)
            .WithName(nameof(ApprovalRuleSaveRequest.ApproverTier)).WithMessage(Messages.ApproverTierRequired);
        RuleFor(x => x.DocType).IsInEnum();
    }
}
