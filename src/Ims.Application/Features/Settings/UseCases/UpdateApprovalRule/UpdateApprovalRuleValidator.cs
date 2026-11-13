using FluentValidation;
namespace Ims.Application.Features.Settings;

public sealed class UpdateApprovalRuleValidator : AbstractValidator<UpdateApprovalRuleCommand>
{
    public UpdateApprovalRuleValidator() => RuleFor(x => x.Data).SetValidator(new ApprovalRuleSaveRequestValidator());
}
