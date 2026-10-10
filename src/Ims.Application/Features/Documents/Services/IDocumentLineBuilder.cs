using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IDocumentLineBuilder
{
    /// <summary>Validates the inputs against the catalogue and converts quantities to base units (BR-04).</summary>
    Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct);
}
