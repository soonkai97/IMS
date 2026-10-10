namespace Ims.Application.Features.Documents;

public record PostDocumentCommand(int Id, PostRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
