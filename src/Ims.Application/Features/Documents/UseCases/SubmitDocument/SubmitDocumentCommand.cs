namespace Ims.Application.Features.Documents;

public record SubmitDocumentCommand(Guid Id, string? IfMatch = null) : ICommand<DocumentDto>;
