namespace Ims.Application.Features.Documents;

public record ListDocumentsQuery(DocumentFilter Filter) : IQuery<PagedResult<DocumentListItem>>;
