using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="AfterId">Seek paging: return documents older than this id (constant cost at any depth).</param>
/// <param name="IncludeTotal">False skips the row count.</param>
public record DocumentFilter(DocumentType? Type, DocumentStatus? Status, int? WarehouseId, DateOnly? From, DateOnly? To,
    string? Search, int Page = 1, int PageSize = Limits.DefaultPageSize, int? AfterId = null, bool IncludeTotal = true);
