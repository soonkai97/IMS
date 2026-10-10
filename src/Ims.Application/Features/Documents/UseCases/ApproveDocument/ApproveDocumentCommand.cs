namespace Ims.Application.Features.Documents;

public record ApproveDocumentCommand(int Id, ApproveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
