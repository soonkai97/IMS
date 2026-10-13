namespace Ims.Application.Features.Documents;

public record GetDocumentQuery(Guid Id) : IQuery<DocumentDto>;
