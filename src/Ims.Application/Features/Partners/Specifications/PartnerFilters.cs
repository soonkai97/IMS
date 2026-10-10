using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

internal static class PartnerFilters
{
    public static void Apply(ISpecificationBuilder<Partner> query, PartnerFilter f)
    {
        query.AsNoTracking();
        if (f.Type is { } t) query.Where(p => p.Type == t);
        if (!f.IncludeInactive) query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query.Where(p => p.Name.StartsWith(s)); // prefix search can use the index on (Type, Name)
        }
    }
}
