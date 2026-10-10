using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>BR-11: allocates the next document number. Must run inside a unit of work so a race retries instead of duplicating.</summary>
public interface IDocumentNumbering
{
    Task<string> NextAsync(DocumentType type, Warehouse warehouse, CancellationToken ct);
}
