namespace Ims.Application.Features.Documents;

public record UpdateDocumentCommand(int Id, DocumentSaveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
