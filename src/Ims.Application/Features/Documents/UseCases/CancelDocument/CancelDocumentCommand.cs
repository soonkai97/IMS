namespace Ims.Application.Features.Documents;

public record CancelDocumentCommand(Guid Id, CancelRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
