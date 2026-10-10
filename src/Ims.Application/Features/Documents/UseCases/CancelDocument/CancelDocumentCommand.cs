namespace Ims.Application.Features.Documents;

public record CancelDocumentCommand(int Id, CancelRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
