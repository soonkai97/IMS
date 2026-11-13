namespace Ims.Application.Features.Documents;

public sealed record CancelDocumentCommand(Guid Id, CancelRequest Data, string? IfMatch = null) : ICommand<DocumentDto>;
