using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentsCountSpec : Specification<StockDocument>
{
    public DocumentsCountSpec(DocumentFilter f, int? warehouseId, IReadOnlyCollection<int>? scope, int userId, DocumentSearch? search) => DocumentFilters.Apply(Query, f, warehouseId, scope, userId, search);
}
