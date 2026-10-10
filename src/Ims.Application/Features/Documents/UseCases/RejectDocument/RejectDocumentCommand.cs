namespace Ims.Application.Features.Documents;

public record RejectDocumentCommand(int Id, RejectRequest Data) : ICommand<DocumentDto>;
