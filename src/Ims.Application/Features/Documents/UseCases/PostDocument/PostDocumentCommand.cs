namespace Ims.Application.Features.Documents;

public record PostDocumentCommand(int Id, string? IfMatch = null) : ICommand<DocumentDto>;
