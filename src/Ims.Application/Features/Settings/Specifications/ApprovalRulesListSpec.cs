using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <param name="hidden">Subjects whose rules are not listed now (a module that is off).</param>
public sealed class ApprovalRulesListSpec : Specification<ApprovalRule>
{
    public ApprovalRulesListSpec(IReadOnlyCollection<DocumentType> hidden)
    {
        Query.AsNoTracking();
        if (hidden.Count > 0) Query.Where(r => !hidden.Contains(r.DocType));
        Query.OrderBy(r => r.DocType).ThenBy(r => r.Id);
    }
}
