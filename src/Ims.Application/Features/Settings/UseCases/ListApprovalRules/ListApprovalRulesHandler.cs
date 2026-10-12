using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class ListApprovalRulesHandler(IReadRepository<ApprovalRule> rules) : IQueryHandler<ListApprovalRulesQuery, IReadOnlyList<ApprovalRuleDto>>
{
    public async Task<IReadOnlyList<ApprovalRuleDto>> HandleAsync(ListApprovalRulesQuery q, CancellationToken ct) =>
        (await rules.ListAsync(new ApprovalRulesListSpec(), ct))
        .Select(r => new ApprovalRuleDto(r.PublicId, r.DocType, r.AmountThreshold, r.QtyThreshold, r.RoleRequired, r.IsActive)).ToList();
}
