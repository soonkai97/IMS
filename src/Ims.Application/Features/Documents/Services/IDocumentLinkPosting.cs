using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// The order part of posting a document that was created from an order, kept out of <c>PostDocumentHandler</c> so that a document that is not linked runs exactly the statements it ran before: for it no
/// implementation is asked at all. One implementation per <see cref="DocumentLinkKind"/> (a receipt from a purchase order, a delivery from a sales order); the handler picks the one that matches the
/// document's link. Lock order: the order's lock FIRST, then the stock locks (taken by the balance loader afterwards), never the other way.
/// </summary>
public interface IDocumentLinkPosting
{
    /// <summary>The link kind this implementation handles.</summary>
    DocumentLinkKind Kind { get; }

    /// <summary>Before the engine: takes the order's lock, reads the order and the posted quantities fresh (the lock is what makes them the truth) and refuses what the order does not allow. The state is handed to <see cref="AfterPost"/>.</summary>
    Task<LinkedPostingState?> BeforePostAsync(StockDocument document, CancellationToken ct);

    /// <summary>After the engine: whatever the order needs once the document has posted (closing it when everything is received or delivered). Does nothing for null.</summary>
    void AfterPost(LinkedPostingState? state, StockDocument document);
}
