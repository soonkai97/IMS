namespace Ims.Application.Features.Documents;

public record UpdateDocumentCommand(Guid Id, DocumentSaveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
