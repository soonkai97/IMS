using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class ActiveRulesForTypeSpec : Specification<ApprovalRule>
{
    public ActiveRulesForTypeSpec(DocumentType type) => Query.Where(r => r.IsActive && r.DocType == type).AsNoTracking();
}
