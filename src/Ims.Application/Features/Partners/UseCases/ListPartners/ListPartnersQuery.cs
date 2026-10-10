namespace Ims.Application.Features.Partners;

public record ListPartnersQuery(PartnerFilter Filter) : IQuery<PagedResult<PartnerDto>>;
