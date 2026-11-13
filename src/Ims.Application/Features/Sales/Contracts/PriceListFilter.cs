using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="Search">Lists whose name starts with the text (case-insensitive).</param>
public sealed record PriceListFilter(string? Search = null, int Page = 1, int PageSize = Limits.DefaultListPageSize);
