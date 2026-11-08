using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The row version of a document as the database has it now (not the tracked copy): what a posting compares after it has taken the order's lock.</summary>
public sealed class DocumentStampSpec : Specification<StockDocument, DocumentStamp>
{
    public DocumentStampSpec(int documentId) => Query.AsNoTracking().Where(d => d.Id == documentId).Select(d => new DocumentStamp(d.RowVersion));
}
