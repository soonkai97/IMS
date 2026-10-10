using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentsCountSpec : Specification<StockDocument>
{
    public DocumentsCountSpec(DocumentFilter f, IReadOnlyCollection<int>? scope, int userId) => DocumentFilters.Apply(Query, f, scope, userId);
}
