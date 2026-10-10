namespace Ims.Application.Features.Documents;

public record SubmitDocumentCommand(int Id, string? IfMatch = null) : ICommand<DocumentDto>;
