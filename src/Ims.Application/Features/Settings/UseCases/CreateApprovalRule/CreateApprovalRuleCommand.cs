namespace Ims.Application.Features.Settings;

public record CreateApprovalRuleCommand(ApprovalRuleSaveRequest Data) : ICommand<ApprovalRuleDto>;
