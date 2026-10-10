namespace Ims.Application.Features.Documents;

public record RejectDocumentCommand(int Id, RejectRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
