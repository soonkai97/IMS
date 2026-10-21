using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>
/// The purchase-order part of posting a receipt that was created from an order (PL2.5), kept out of <c>PostDocumentHandler</c> so that a document that is not linked runs exactly the statements
/// it ran before: for it both methods answer at once without a query. Lock order: the order's lock FIRST, then the stock locks (taken by the balance loader afterwards), never the other way.
/// </summary>
public interface ILinkedReceiptPosting
{
    /// <summary>
    /// For a linked receipt: takes <c>po:{id}</c>, then reads the order and the posted quantities fresh (the lock is what makes them the truth), and refuses when the Purchasing module is off (409), the
    /// order is no longer Approved (409, names the status) or the receipt would take a line over what remains (409 <see cref="OverReceiptException"/>, names the remaining quantity per line).
    /// Null for a receipt that is not linked.
    /// </summary>
    Task<LinkedPosting?> BeforePostAsync(StockDocument document, CancellationToken ct);

    /// <summary>After the engine: closes the order (kind Received, an audit event naming the poster) when this receipt brought in the last open quantity of every line. Does nothing for null.</summary>
    void AfterPost(LinkedPosting? linked, StockDocument document);
}
