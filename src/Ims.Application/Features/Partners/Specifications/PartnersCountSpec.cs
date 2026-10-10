using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public sealed class PartnersCountSpec : Specification<Partner>
{
    public PartnersCountSpec(PartnerFilter f) => PartnerFilters.Apply(Query, f);
}
