namespace Ims.Application.Features.Documents;

public record GetDocumentQuery(int Id) : IQuery<DocumentDto>;
