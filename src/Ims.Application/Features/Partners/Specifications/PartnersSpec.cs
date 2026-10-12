using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <summary>One page of partners ordered by name.</summary>
public sealed class PartnersSpec : Specification<Partner, PartnerDto>
{
    public PartnersSpec(PartnerFilter f)
    {
        PartnerFilters.Apply(Query, f);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new PartnerDto(p.PublicId, p.Type, p.Name, p.Contact, p.IsActive));
    }
}
