using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public sealed class UpdateApprovalRuleHandler(IRepository<ApprovalRule> rules, IModuleSwitches modules, ITransactionLocker locker, IUnitOfWork uow) : ICommandHandler<UpdateApprovalRuleCommand, ApprovalRuleDto>
{
    public Task<ApprovalRuleDto> HandleAsync(UpdateApprovalRuleCommand cmd, CancellationToken ct) => uow.ExecuteAsync(async () =>
    {
        var rule = await rules.FirstOrDefaultAsync(new ByPublicIdSpec<ApprovalRule>(cmd.Id), ct) ?? throw new NotFoundException(Messages.RuleNotFound);
        // A rule of an off module is kept but hidden: it cannot be found by id either, and nothing can be moved onto such a subject.
        var moduleOn = !(await modules.HiddenTypesAsync(ct)).Contains(rule.DocType);
        if (!moduleOn) throw new NotFoundException(Messages.RuleNotFound);
        await modules.EnsureTypeAvailableAsync(cmd.Data.DocType, ct);
        // The rules that span the rules of a type (ApprovalRuleSetPolicy) decide; the handler reads the facts. Two people switching off the two different rules of a type would each count two, so the
        // count is taken under the modules lock (the lock of the switch-on check too), which only a transaction can hold, and only when the policy says the answer depends on it.
        var activeRules = 0;
        if (ApprovalRuleSetPolicy.NeedsActiveRuleCount(rule, cmd.Data.DocType, cmd.Data.IsActive, moduleOn))
        {
            await locker.LockModulesAsync(ct);
            activeRules = await rules.CountAsync(new ActiveRulesForTypeSpec(rule.DocType), ct);
        }
        var refusal = ApprovalRuleSetPolicy.Refusal(rule, cmd.Data.DocType, cmd.Data.IsActive, moduleOn, activeRules);
        if (refusal != ApprovalRuleRefusalReason.None) throw RefusalOf(refusal);
        rule.Update(cmd.Data.DocType, cmd.Data.AmountThreshold, cmd.Data.QtyThreshold, cmd.Data.ApproverTier!.Value, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return CreateApprovalRuleHandler.ToDto(rule);
    }, ct);

    /// <summary>The sentence and the status of each refusal: a rule that keeps its type is a 400, the last active rule a 409.</summary>
    private static Exception RefusalOf(ApprovalRuleRefusalReason refusal) => refusal switch
    {
        ApprovalRuleRefusalReason.PurchaseOrderRuleTypeFixed => new ValidationException(Messages.PurchaseOrderRuleTypeFixed),
        ApprovalRuleRefusalReason.SalesOrderRuleTypeFixed => new ValidationException(Messages.SalesOrderRuleTypeFixed),
        ApprovalRuleRefusalReason.LastPurchaseOrderRule => new ConflictException(Messages.PurchaseOrderLastRule),
        ApprovalRuleRefusalReason.LastSalesOrderRule => new ConflictException(Messages.SalesOrderLastRule),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "No refusal to map."),
    };
}
