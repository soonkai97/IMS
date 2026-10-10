namespace Ims.Application.Features.Documents;

public record SubmitDocumentCommand(int Id) : ICommand<DocumentDto>;
