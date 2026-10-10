using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed class DocumentScopeSpec : Specification<StockDocument, DocumentScope>
{
    public DocumentScopeSpec(int documentId) =>
        Query.AsNoTracking().Where(d => d.Id == documentId).Select(d => new DocumentScope(d.WarehouseId, d.Status));
}
