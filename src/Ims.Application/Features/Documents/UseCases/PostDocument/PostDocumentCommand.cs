namespace Ims.Application.Features.Documents;

public sealed record PostDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
