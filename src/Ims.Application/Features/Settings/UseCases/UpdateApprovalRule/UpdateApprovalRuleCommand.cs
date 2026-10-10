namespace Ims.Application.Features.Settings;

public record UpdateApprovalRuleCommand(int Id, ApprovalRuleSaveRequest Data) : ICommand<ApprovalRuleDto>;
