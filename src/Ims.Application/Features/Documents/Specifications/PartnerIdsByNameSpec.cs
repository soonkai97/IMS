using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// Ids of the partners whose name contains the text, for the document search box (partners are few, so this is a small scan). It reads one more than
/// <see cref="Limits.SearchPartnerListMax"/>, which is enough to tell "a short list" from "too many for a list": the caller then tests the name through a join
/// and does not use the ids at all (nothing is ever cut off).
/// </summary>
public sealed class PartnerIdsByNameSpec : Specification<Partner, int>
{
    public PartnerIdsByNameSpec(string text) =>
        Query.AsNoTracking().Where(p => p.Name.Contains(text)).OrderBy(p => p.Id).Take(Limits.SearchPartnerListMax + 1).Select(p => p.Id);
}
