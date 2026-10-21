using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class UpdateApprovalRuleHandler(IRepository<ApprovalRule> rules, IModuleSwitches modules, ITransactionLocker locker, IUnitOfWork uow) : ICommandHandler<UpdateApprovalRuleCommand, ApprovalRuleDto>
{
    public Task<ApprovalRuleDto> HandleAsync(UpdateApprovalRuleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var rule = await rules.FirstOrDefaultAsync(new ByPublicIdSpec<ApprovalRule>(cmd.Id), ct) ?? throw new NotFoundException(Messages.RuleNotFound);
        // A rule of an off module is kept but hidden: it cannot be found by id either, and nothing can be moved onto such a subject.
        if ((await modules.HiddenTypesAsync(ct)).Contains(rule.DocType)) throw new NotFoundException(Messages.RuleNotFound);
        await modules.EnsureTypeAvailableAsync(cmd.Data.DocType, ct);
        // A purchase-order rule stays a purchase-order rule, and no other rule becomes one: the two tiers for orders are set up on purpose, not by editing a rule of another type.
        if ((rule.DocType == DocumentType.PurchaseOrder) != (cmd.Data.DocType == DocumentType.PurchaseOrder)) throw new ValidationException(Messages.PurchaseOrderRuleTypeFixed);
        // While purchasing is on, the last active rule cannot be switched off: with none every priced order is released with no approver. Two people switching off the two different rules
        // would each count two, so the count is taken under the modules lock (the lock of the switch-on check too), which only a transaction can hold.
        if (rule.DocType == DocumentType.PurchaseOrder && rule.IsActive && !cmd.Data.IsActive)
        {
            await locker.LockModulesAsync(ct);
            if (await rules.CountAsync(new ActiveRulesForTypeSpec(DocumentType.PurchaseOrder), ct) <= 1) throw new ConflictException(Messages.PurchaseOrderLastRule);
        }
        rule.Update(cmd.Data.DocType, cmd.Data.AmountThreshold, cmd.Data.QtyThreshold, cmd.Data.ApproverTier!.Value, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return CreateApprovalRuleHandler.ToDto(rule);
    }, ct);
}
