namespace Ims.Application.Features.Documents;

public class GetDocumentHandler(IDocumentReader reader) : IQueryHandler<GetDocumentQuery, DocumentDto>
{
    public Task<DocumentDto> HandleAsync(GetDocumentQuery q, CancellationToken ct) => reader.GetAsync(q.Id, ct);
}
