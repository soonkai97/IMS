using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Ids of the partners whose name contains the text, for the document search box (partners are few, so this is a small scan; the documents are then matched by <c>PartnerId IN (...)</c>).</summary>
public sealed class PartnerIdsByNameSpec : Specification<Partner, int>
{
    public PartnerIdsByNameSpec(string text) =>
        Query.AsNoTracking().Where(p => p.Name.Contains(text)).OrderBy(p => p.Id).Take(Limits.SearchPartnerMatches).Select(p => p.Id);
}
