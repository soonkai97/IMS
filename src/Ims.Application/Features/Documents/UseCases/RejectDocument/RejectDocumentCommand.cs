namespace Ims.Application.Features.Documents;

public sealed record RejectDocumentCommand(Guid Id, RejectRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
