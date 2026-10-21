using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class UpdateApprovalRuleHandler(IRepository<ApprovalRule> rules, IModuleSwitches modules, IUnitOfWork uow) : ICommandHandler<UpdateApprovalRuleCommand, ApprovalRuleDto>
{
    public async Task<ApprovalRuleDto> HandleAsync(UpdateApprovalRuleCommand cmd, CancellationToken ct)
    {
        var rule = await rules.FirstOrDefaultAsync(new ByPublicIdSpec<ApprovalRule>(cmd.Id), ct) ?? throw new NotFoundException(Messages.RuleNotFound);
        // A rule of an off module is kept but hidden: it cannot be found by id either, and nothing can be moved onto such a subject.
        if ((await modules.HiddenTypesAsync(ct)).Contains(rule.DocType)) throw new NotFoundException(Messages.RuleNotFound);
        await modules.EnsureTypeAvailableAsync(cmd.Data.DocType, ct);
        rule.Update(cmd.Data.DocType, cmd.Data.AmountThreshold, cmd.Data.QtyThreshold, cmd.Data.ApproverTier!.Value, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return CreateApprovalRuleHandler.ToDto(rule);
    }
}
