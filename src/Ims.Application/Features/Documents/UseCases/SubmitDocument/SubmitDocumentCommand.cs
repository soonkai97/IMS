namespace Ims.Application.Features.Documents;

public sealed record SubmitDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
