namespace Ims.Application.Features.Documents;

public sealed record ApproveDocumentCommand(Guid Id, ApproveRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
