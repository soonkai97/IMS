namespace Ims.Application.Features.Settings;

public sealed record CreateApprovalRuleCommand(ApprovalRuleSaveRequest Data) : ICommand<ApprovalRuleDto>;
