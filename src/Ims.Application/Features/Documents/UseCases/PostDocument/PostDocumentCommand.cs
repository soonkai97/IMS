namespace Ims.Application.Features.Documents;

public record PostDocumentCommand(int Id, PostRequest Data) : ICommand<DocumentDto>;
