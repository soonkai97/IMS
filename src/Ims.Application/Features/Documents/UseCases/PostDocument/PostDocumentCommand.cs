namespace Ims.Application.Features.Documents;

public record PostDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
