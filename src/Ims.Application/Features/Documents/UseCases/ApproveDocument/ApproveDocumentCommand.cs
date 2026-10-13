namespace Ims.Application.Features.Documents;

public record ApproveDocumentCommand(Guid Id, ApproveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
