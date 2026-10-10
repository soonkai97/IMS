using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class CreateApprovalRuleHandler(IRepository<ApprovalRule> rules, IUnitOfWork uow) : ICommandHandler<CreateApprovalRuleCommand, ApprovalRuleDto>
{
    public async Task<ApprovalRuleDto> HandleAsync(CreateApprovalRuleCommand cmd, CancellationToken ct)
    {
        var r = cmd.Data;
        var rule = ApprovalRule.Create(r.DocType, r.AmountThreshold, r.QtyThreshold, r.RoleRequired, r.IsActive);
        rules.Add(rule);
        await uow.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    internal static ApprovalRuleDto ToDto(ApprovalRule r) => new(r.Id, r.DocType, r.AmountThreshold, r.QtyThreshold, r.RoleRequired, r.IsActive);
}
