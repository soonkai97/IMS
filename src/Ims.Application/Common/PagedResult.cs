namespace Ims.Application;

/// <summary>One page of a server-side paged list.</summary>
/// <param name="Total">Rows matching the filter, or <see cref="Paging.TotalNotCounted"/> when the caller asked to skip the (expensive) count.</param>
/// <param name="NextCursor">Id to pass as <c>afterId</c> for the next page of a seek-paged list; null when there is no further page or the list is not seek-paged.</param>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, long? NextCursor = null);
