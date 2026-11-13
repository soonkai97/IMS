namespace Ims.Application.Features.Documents;

public sealed record GetDocumentQuery(Guid Id) : IQuery<DocumentDto>;
