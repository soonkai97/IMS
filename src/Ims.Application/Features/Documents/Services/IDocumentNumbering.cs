using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>BR-11: allocates the next document number. Must run inside a unit of work so a race retries instead of duplicating.</summary>
public interface IDocumentNumbering
{
    Task<string> NextAsync(DocumentType type, Warehouse warehouse, CancellationToken ct);

    /// <summary>The next number of any sequence named by its prefix (a purchase order has the prefix <see cref="DocumentNumberFormat.PurchaseOrderPrefix"/>): the same lock, the same sequence table and the same format as a document.</summary>
    Task<string> NextAsync(string prefix, Warehouse warehouse, CancellationToken ct);
}
