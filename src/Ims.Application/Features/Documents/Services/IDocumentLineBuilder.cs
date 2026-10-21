using Ims.Domain;
namespace Ims.Application.Features.Documents;

public interface IDocumentLineBuilder
{
    /// <summary>Validates the inputs against the catalogue and converts quantities to base units (BR-04).</summary>
    /// <param name="existing">The lines of the draft being saved again, if any: a line that was at the average and comes back without a cost stays at the average.</param>
    /// <param name="order">The purchase order of a receipt created from one (loaded with its lines, products and units); null for every other document. Every input must then name a line of it by <c>PurchaseOrderLineNo</c> with the line's product and unit, and a line is priced at the order's price unless the caller may enter cost and sent one. Nothing is said here about quantities: the remaining quantity is checked by <see cref="Ims.Application.Features.Purchasing.IReceiptLinkRules"/>.</param>
    /// <exception cref="ValidationException">Any input is wrong; for a linked receipt every wrong line is reported, not the first.</exception>
    Task<List<StockDocumentLine>> BuildAsync(DocumentType type, IReadOnlyList<DocumentLineInput> inputs, CancellationToken ct, IEnumerable<StockDocumentLine>? existing = null,
        PurchaseOrder? order = null);
}
