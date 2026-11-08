using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The rules for saving and submitting a delivery that was created from a sales order (PL3.5), the twin of the receipt link rules. The other rules of a linked line (it names an order line, with that line's product and unit) are the line builder's.</summary>
public interface ISalesDeliveryRules
{
    /// <summary>
    /// Loads the order of a linked Draft delivery for a save and checks the header: the draft is still editable, the caller may read sales orders and the Sales module is on, the order is still Approved,
    /// the customer is the order's and the purpose is Sale (the warehouse cannot change on any document). The link itself is not a field of the request and is never changed.
    /// </summary>
    /// <exception cref="ConflictException">The module is off, or the order is no longer Approved (409, with the status).</exception>
    /// <exception cref="ForbiddenException">The caller may not read sales orders.</exception>
    /// <exception cref="ValidationException">The customer is not the order's.</exception>
    Task<SalesOrder> OrderForSaveAsync(StockDocument draft, Partner? customer, IssuePurpose? purpose, CancellationToken ct);

    /// <summary>
    /// The quantities of the lines, added up per order line, must not exceed what remains of it (the order's quantity less what Posted deliveries have delivered). Drafts and Submitted deliveries reserve
    /// nothing (D6), so this is the early answer for the person typing; the check that decides is made when a delivery posts (PL3.7), under the order's lock.
    /// </summary>
    /// <exception cref="ValidationException">Names every order line that is over, with the remaining quantity (400).</exception>
    Task EnsureWithinRemainingAsync(SalesOrder order, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct);

    /// <summary>Submitting a delivery asks the same again: the module is on, the order is still Approved and every line is within what remains. Does nothing for a document that is not a delivery.</summary>
    Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct);
}
