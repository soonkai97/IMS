namespace Ims.Application.Features.Documents;

public sealed record RecallDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
