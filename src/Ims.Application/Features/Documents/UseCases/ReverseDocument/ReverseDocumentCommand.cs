namespace Ims.Application.Features.Documents;

public record ReverseDocumentCommand(int Id, ReverseRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
