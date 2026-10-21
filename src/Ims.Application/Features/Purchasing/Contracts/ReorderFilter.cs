using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <param name="WarehouseId">Required: the ship-to warehouse the suggestions are for. 404 when it does not exist or the caller may not read it.</param>
/// <param name="Search">Products whose SKU or name contains the text (case-insensitive).</param>
public record ReorderFilter(Guid WarehouseId, string? Search = null, int Page = 1, int PageSize = Limits.DefaultListPageSize);
