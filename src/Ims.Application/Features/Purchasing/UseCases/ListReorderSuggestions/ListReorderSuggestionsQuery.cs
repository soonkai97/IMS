namespace Ims.Application.Features.Purchasing;

public record ListReorderSuggestionsQuery(ReorderFilter Filter) : IQuery<PagedResult<ReorderSuggestion>>;
