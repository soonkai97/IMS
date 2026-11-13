namespace Ims.Application.Features.Documents;

public sealed record ListDocumentsQuery(DocumentFilter Filter) : IQuery<PagedResult<DocumentListItem>>;
