using Ims.Domain;
namespace Ims.Application.Features.Partners;

public sealed record PartnerFilter(PartnerType? Type, bool IncludeInactive = false, string? Search = null, int Page = 1, int PageSize = Limits.DefaultListPageSize);
