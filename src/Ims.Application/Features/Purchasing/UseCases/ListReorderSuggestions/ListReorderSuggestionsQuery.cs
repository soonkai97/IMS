namespace Ims.Application.Features.Purchasing;

public sealed record ListReorderSuggestionsQuery(ReorderFilter Filter) : IQuery<PagedResult<ReorderSuggestion>>;
