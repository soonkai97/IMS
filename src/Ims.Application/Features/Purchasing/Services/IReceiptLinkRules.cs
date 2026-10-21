using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>The rules for saving a receipt that was created from a purchase order (PL2.4). The other rules of a linked line (it names an order line, with that line's product and unit, at its price) are the line builder's.</summary>
public interface IReceiptLinkRules
{
    /// <summary>
    /// Loads the order of a linked Draft for a save and checks the header: the draft is still editable, the Purchasing module is on, the order is still Approved, and the supplier is the order's
    /// (the warehouse cannot change on any document). The link itself is not a field of the request and is never changed.
    /// </summary>
    /// <param name="supplier">The partner the save names.</param>
    /// <exception cref="ConflictException">The module is off, or the order is no longer Approved (409, with the status).</exception>
    /// <exception cref="ValidationException">The supplier is not the order's.</exception>
    Task<PurchaseOrder> OrderForSaveAsync(StockDocument draft, Partner? supplier, CancellationToken ct);

    /// <summary>
    /// The quantities of the lines, added up per order line, must not exceed what remains of it (the order's quantity less what Posted receipts have received). Drafts and Submitted receipts reserve
    /// nothing, so this is the early answer for the person typing; the check that decides is made when a receipt posts (PL2.5), under the order's lock.
    /// </summary>
    /// <exception cref="ValidationException">Names every order line that is over, with the remaining quantity (400).</exception>
    Task EnsureWithinRemainingAsync(PurchaseOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct);
}
