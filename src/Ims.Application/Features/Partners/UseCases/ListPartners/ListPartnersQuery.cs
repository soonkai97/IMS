namespace Ims.Application.Features.Partners;

public sealed record ListPartnersQuery(PartnerFilter Filter) : IQuery<PagedResult<PartnerDto>>;
