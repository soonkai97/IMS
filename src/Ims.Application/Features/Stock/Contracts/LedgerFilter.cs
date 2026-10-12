using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <param name="Cursor">Seek paging: the <c>nextCursor</c> of the previous page, unchanged; returns the rows after it. Use it instead of <c>Page</c> for deep pages (constant cost at any depth).</param>
/// <param name="IncludeTotal">False skips the row count, which is the expensive part of a very large ledger.</param>
public record LedgerFilter(Guid? WarehouseId, int? ProductId, DateOnly? From, DateOnly? To, int? SourceDocId,
    int Page = 1, int PageSize = Limits.DefaultListPageSize, string? Cursor = null, bool IncludeTotal = true);
