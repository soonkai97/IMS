namespace Ims.Application.Features.Settings;

public sealed record UpdateApprovalRuleCommand(Guid Id, ApprovalRuleSaveRequest Data) : ICommand<ApprovalRuleDto>;
