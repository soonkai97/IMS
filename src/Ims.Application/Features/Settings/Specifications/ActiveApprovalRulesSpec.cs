using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public sealed class ActiveApprovalRulesSpec : Specification<ApprovalRule>
{
    public ActiveApprovalRulesSpec(DocumentType type) => Query.Where(r => r.IsActive && r.DocType == type).AsNoTracking();
}
