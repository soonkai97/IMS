using Ims.Domain;
namespace Ims.Application.Features.Sales;

public interface ISalesOrderPriceAudit
{
    /// <summary>
    /// Writes one <c>sales-order.price-changed</c> event for every line of a draft that is being saved whose price differs from the one the customer's price list (else the default list) gives for its product and
    /// unit, and that is NEW or REPRICED against the previous save. A line the previous save already had, unchanged, writes nothing (a re-save of unchanged lines repeats nothing, and neither does a list that
    /// changed in between); a customer change counts every line as new. A line with no price yet (0.00) and a line with no list price write nothing. The events join the caller's unit of work.
    /// </summary>
    /// <param name="before">The lines of the previous save (empty for a new order).</param>
    /// <param name="beforeCustomerId">The customer of the previous save (null for a new order).</param>
    Task RecordAsync(SalesOrder order, IReadOnlyCollection<SavedLine> before, int? beforeCustomerId, CancellationToken ct);
}
