using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class CreateApprovalRuleHandler(IRepository<ApprovalRule> rules, IModuleSwitches modules, IUnitOfWork uow) : ICommandHandler<CreateApprovalRuleCommand, ApprovalRuleDto>
{
    public async Task<ApprovalRuleDto> HandleAsync(CreateApprovalRuleCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        await modules.EnsureTypeAvailableAsync(r.DocType, ct);
        var rule = ApprovalRule.Create(r.DocType, r.AmountThreshold, r.QtyThreshold, r.ApproverTier!.Value, r.IsActive);
        rules.Add(rule);
        await uow.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    internal static ApprovalRuleDto ToDto(ApprovalRule r) => new(r.PublicId, r.DocType, r.AmountThreshold, r.QtyThreshold, r.Tier, ApproverTierNames.RoleOf(r.Tier), r.IsActive);
}
