namespace Ims.Application.Features.Documents;

public record ApproveDocumentCommand(int Id) : ICommand<DocumentDto>;
