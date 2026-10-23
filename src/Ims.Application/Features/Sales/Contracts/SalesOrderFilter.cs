using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="CustomerId">Orders of this customer; an id that names no customer matches nothing.</param>
/// <param name="WarehouseId">Orders for this ship-from warehouse. A warehouse the caller may not read matches nothing.</param>
/// <param name="From">Orders created on or after this date (Malaysia time).</param>
/// <param name="To">Orders created on or before this date (Malaysia time).</param>
/// <param name="Search">Orders whose number contains the text (case-insensitive; a partial number such as 0100 works).</param>
/// <param name="Cursor">Seek paging: the <c>nextCursor</c> of the previous page, unchanged; returns the orders after it.</param>
/// <param name="IncludeTotal">False skips the row count.</param>
public record SalesOrderFilter(SalesOrderStatus? Status, Guid? CustomerId, Guid? WarehouseId, DateOnly? From, DateOnly? To, string? Search,
    int Page = 1, int PageSize = Limits.DefaultPageSize, string? Cursor = null, bool IncludeTotal = true);
