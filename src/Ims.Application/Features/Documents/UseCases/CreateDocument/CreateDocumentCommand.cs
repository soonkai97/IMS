using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed record CreateDocumentCommand(DocumentType Type, DocumentSaveRequest Data) : ICommand<DocumentDto>;
