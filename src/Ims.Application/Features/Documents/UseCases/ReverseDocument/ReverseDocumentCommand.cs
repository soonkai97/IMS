namespace Ims.Application.Features.Documents;

public record ReverseDocumentCommand(int Id, ReverseRequest Data) : ICommand<DocumentDto>;
