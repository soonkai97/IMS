using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class UpdateApprovalRuleHandler(IRepository<ApprovalRule> rules, IUnitOfWork uow) : ICommandHandler<UpdateApprovalRuleCommand, ApprovalRuleDto>
{
    public async Task<ApprovalRuleDto> HandleAsync(UpdateApprovalRuleCommand cmd, CancellationToken ct)
    {
        var rule = await rules.FirstOrDefaultAsync(new ByPublicIdSpec<ApprovalRule>(cmd.Id), ct) ?? throw new NotFoundException(Messages.RuleNotFound);
        rule.Update(cmd.Data.DocType, cmd.Data.AmountThreshold, cmd.Data.QtyThreshold, ApproverTierNames.Resolve(cmd.Data.RoleRequired, cmd.Data.ApproverTier)!.Value, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return CreateApprovalRuleHandler.ToDto(rule);
    }
}
