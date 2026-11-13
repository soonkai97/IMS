using FluentValidation;
namespace Ims.Application.Features.Settings;

public sealed class CreateApprovalRuleValidator : AbstractValidator<CreateApprovalRuleCommand>
{
    public CreateApprovalRuleValidator() => RuleFor(x => x.Data).SetValidator(new ApprovalRuleSaveRequestValidator());
}
