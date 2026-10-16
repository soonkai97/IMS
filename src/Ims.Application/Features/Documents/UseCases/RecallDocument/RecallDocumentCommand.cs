namespace Ims.Application.Features.Documents;

public record RecallDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
