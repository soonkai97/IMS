using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public sealed class ApprovalRulesListSpec : Specification<ApprovalRule>
{
    public ApprovalRulesListSpec() => Query.AsNoTracking().OrderBy(r => r.DocType).ThenBy(r => r.Id);
}
