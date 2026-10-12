namespace Ims.Application.Features.Documents;

public record RejectDocumentCommand(Guid Id, RejectRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
