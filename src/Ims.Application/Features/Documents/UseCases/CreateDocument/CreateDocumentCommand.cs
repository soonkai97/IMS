using Ims.Domain;
namespace Ims.Application.Features.Documents;

public record CreateDocumentCommand(DocumentType Type, DocumentSaveRequest Data) : ICommand<DocumentDto>;
