using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IDocumentLineBuilder
{
    /// <summary>Validates the inputs against the catalogue and converts quantities to base units (BR-04).</summary>
    /// <param name="existing">The lines of the draft being saved again, if any: a line that was at the average and comes back without a cost stays at the average.</param>
    Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct, IEnumerable<StockDocumentLine>? existing = null);
}
