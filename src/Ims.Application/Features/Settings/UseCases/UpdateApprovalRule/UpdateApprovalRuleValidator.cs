using FluentValidation;
namespace Ims.Application.Features.Settings;

public class UpdateApprovalRuleValidator : AbstractValidator<UpdateApprovalRuleCommand>
{
    public UpdateApprovalRuleValidator() => RuleFor(x => x.Data).SetValidator(new ApprovalRuleSaveRequestValidator());
}
