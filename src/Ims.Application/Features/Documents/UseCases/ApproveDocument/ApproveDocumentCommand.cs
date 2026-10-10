namespace Ims.Application.Features.Documents;

public record ApproveDocumentCommand(int Id, string? IfMatch = null) : ICommand<DocumentDto>;
