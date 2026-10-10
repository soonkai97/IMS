namespace Ims.Application.Features.Documents;

public record CancelDocumentCommand(int Id, CancelRequest Data) : ICommand<DocumentDto>;
