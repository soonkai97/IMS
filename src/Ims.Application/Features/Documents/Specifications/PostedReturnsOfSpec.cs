using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>The Posted returns of a document: a seek on the filtered index of <c>StockDocument.ReturnOfDocumentId</c>.</summary>
public sealed class PostedReturnsOfSpec : Specification<StockDocument>
{
    public PostedReturnsOfSpec(int documentId) => Query.AsNoTracking().Where(d => d.ReturnOfDocumentId == documentId && d.Status == DocumentStatus.Posted);
}
