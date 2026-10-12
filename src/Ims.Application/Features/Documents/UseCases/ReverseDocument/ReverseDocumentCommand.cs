namespace Ims.Application.Features.Documents;

public record ReverseDocumentCommand(Guid Id, ReverseRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
