namespace Ims.Application.Features.Settings;

public record UpdateApprovalRuleCommand(Guid Id, ApprovalRuleSaveRequest Data) : ICommand<ApprovalRuleDto>;
