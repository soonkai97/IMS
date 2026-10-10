using FluentValidation;
namespace Ims.Application.Features.Settings;

public class CreateApprovalRuleValidator : AbstractValidator<CreateApprovalRuleCommand>
{
    public CreateApprovalRuleValidator() => RuleFor(x => x.Data).SetValidator(new ApprovalRuleSaveRequestValidator());
}
