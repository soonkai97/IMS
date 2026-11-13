namespace Ims.Application.Features.Documents;

public sealed record ReverseDocumentCommand(Guid Id, ReverseRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
