namespace Ims.Application.Features.Documents;

public sealed record UpdateDocumentCommand(Guid Id, DocumentSaveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
