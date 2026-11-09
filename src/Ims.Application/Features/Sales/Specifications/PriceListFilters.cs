using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

internal static class PriceListFilters
{
    public static void Apply(ISpecificationBuilder<PriceList> query, PriceListFilter f)
    {
        query.AsNoTracking();
        if (f.Search?.Trim() is { Length: > 0 } text) query.Where(l => l.Name.StartsWith(text));
    }
}
