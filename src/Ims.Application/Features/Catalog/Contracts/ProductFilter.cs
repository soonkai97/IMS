using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public record ProductFilter(string? Search, int? CategoryId, bool? Active, int Page = 1, int PageSize = Limits.DefaultListPageSize,
    ProductSort SortBy = ProductSort.Sku, bool Desc = false);
